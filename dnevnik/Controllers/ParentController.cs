using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dnevnik.Data;
using dnevnik.Entities;

namespace dnevnik.Controllers;

[ApiController]
[Route("api/parent")]
[Authorize(Roles = "Родитель,Parent")]
public class ParentController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ParentController> _logger;

    public ParentController(ApplicationDbContext context, ILogger<ParentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst("userId")!.Value);

    private async Task<Parent?> GetCurrentParentAsync()
    {
        var uid = GetUserId();
        var p = await _context.Parents.FirstOrDefaultAsync(x => x.UserId == uid);
        if (p == null) _logger.LogWarning("Профиль родителя не найден для UserId={UserId}", uid);
        return p;
    }

    private async Task<bool> HasAccessToAsync(int parentId, int studentId)
    {
        var ok = await _context.ParentStudents.AnyAsync(ps => ps.ParentId == parentId && ps.StudentId == studentId);
        if (!ok)
            _logger.LogWarning("Доступ запрещён: ParentId={ParentId} не связан со StudentId={StudentId}",
                parentId, studentId);
        return ok;
    }

    // ==================== ХЕЛПЕРЫ ДЛЯ ЧЕТВЕРТЕЙ ====================

    private async Task<Quarter?> GetQuarterForDateAsync(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        return await _context.Quarters
            .Where(q => q.DateStart <= d && q.DateEnd >= d)
            .OrderBy(q => q.QuarterNumber)
            .FirstOrDefaultAsync();
    }

    private async Task<Quarter?> GetCurrentQuarterAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var current = await _context.Quarters
            .Where(q => q.DateStart <= today && q.DateEnd >= today)
            .OrderBy(q => q.QuarterNumber)
            .FirstOrDefaultAsync();
        if (current != null) return current;

        return await _context.Quarters
            .Where(q => q.DateStart <= today)
            .OrderByDescending(q => q.DateStart)
            .FirstOrDefaultAsync();
    }

    /// <summary>Если передан quarterId — берёт его границы, иначе возвращает переданные from/to.</summary>
    private async Task<(DateTime? from, DateTime? to)> ResolveRangeAsync(int? quarterId,
                                                                         DateTime? from, DateTime? to)
    {
        if (quarterId.HasValue)
        {
            var q = await _context.Quarters.FindAsync(quarterId.Value);
            if (q != null)
            {
                from = new DateTime(q.DateStart.Year, q.DateStart.Month, q.DateStart.Day);
                to = new DateTime(q.DateEnd.Year, q.DateEnd.Month, q.DateEnd.Day, 23, 59, 59);
            }
        }
        return (from, to);
    }

    // ==================== ДЕТИ ====================

    [HttpGet("children")]
    public async Task<IActionResult> GetChildren()
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound(new { message = "Профиль родителя не найден" });

        var children = await _context.ParentStudents
            .Where(ps => ps.ParentId == p.Id)
            .Include(ps => ps.Student).ThenInclude(s => s!.User)
            .Include(ps => ps.Student).ThenInclude(s => s!.Class)
            .Select(ps => new
            {
                Id = ps.Student!.Id,
                Name = ps.Student.User!.FullName,
                ClassId = ps.Student.ClassId,
                ClassName = ps.Student.Class!.Name,
                ps.Student.BirthDate
            })
            .ToListAsync();

        _logger.LogInformation("Родитель {ParentId}: получено {Count} детей", p.Id, children.Count);
        return Ok(children);
    }

    // ==================== ЧЕТВЕРТИ ====================

    [HttpGet("quarters")]
    public async Task<IActionResult> GetQuarters()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await _context.Quarters.AsNoTracking()
            .OrderByDescending(q => q.AcademicYear)
            .ThenBy(q => q.QuarterNumber)
            .ToListAsync();

        return Ok(items.Select(q => new
        {
            q.Id,
            q.AcademicYear,
            q.QuarterNumber,
            q.DateStart,
            q.DateEnd,
            IsCurrent = q.DateStart <= today && q.DateEnd >= today,
            Name = $"{q.QuarterNumber}-я четверть",
            YearLabel = $"{q.AcademicYear}/{q.AcademicYear + 1}",
            FullName = $"{q.QuarterNumber}-я четверть ({q.AcademicYear}/{q.AcademicYear + 1})"
        }));
    }

    // ==================== ОЦЕНКИ ====================

    [HttpGet("children/{studentId:int}/grades")]
    public async Task<IActionResult> GetGrades(int studentId,
                                               [FromQuery] DateTime? from,
                                               [FromQuery] DateTime? to,
                                               [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        (from, to) = await ResolveRangeAsync(quarterId, from, to);

        var q = _context.Grades
            .Include(g => g.Subject)
            .Include(g => g.Teacher).ThenInclude(t => t!.User)
            .Where(g => g.StudentId == studentId);

        if (from.HasValue) q = q.Where(g => g.Date >= from);
        if (to.HasValue) q = q.Where(g => g.Date <= to);

        var result = await q.OrderByDescending(g => g.Date).ToListAsync();
        return Ok(result);
    }

    // ==================== ДОМАШНИЕ ЗАДАНИЯ ====================

    [HttpGet("children/{studentId:int}/homework")]
    public async Task<IActionResult> GetHomework(int studentId, [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student == null) return NotFound();

        var (from, to) = await ResolveRangeAsync(quarterId, null, null);

        var hwQuery = _context.Homeworks
            .Include(h => h.Subject)
            .Where(h => h.ClassId == student.ClassId);

        if (from.HasValue) hwQuery = hwQuery.Where(h => h.DateDue >= from);
        if (to.HasValue) hwQuery = hwQuery.Where(h => h.DateIssued <= to);

        var hw = await hwQuery
            .OrderByDescending(h => h.DateIssued)
            .Select(h => new
            {
                h.Id,
                h.Description,
                h.DateIssued,
                h.DateDue,
                Subject = h.Subject!.Name,
                Status = h.HomeworkStatuses
                    .Where(hs => hs.StudentId == studentId)
                    .Select(hs => hs.Status)
                    .FirstOrDefault(),
                DateSubmitted = h.HomeworkStatuses
                    .Where(hs => hs.StudentId == studentId)
                    .Select(hs => hs.DateSubmitted)
                    .FirstOrDefault()
            })
            .ToListAsync();

        return Ok(hw);
    }

    // ==================== ПОСЕЩАЕМОСТЬ ====================

    [HttpGet("children/{studentId:int}/attendance")]
    public async Task<IActionResult> GetAttendance(int studentId,
                                                   [FromQuery] DateTime? from,
                                                   [FromQuery] DateTime? to,
                                                   [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        (from, to) = await ResolveRangeAsync(quarterId, from, to);

        var q = _context.Attendances
            .Include(a => a.Subject)
            .Where(a => a.StudentId == studentId);

        if (from.HasValue) q = q.Where(a => a.Date >= from);
        if (to.HasValue) q = q.Where(a => a.Date <= to);

        var result = await q.OrderByDescending(a => a.Date).ToListAsync();
        return Ok(result);
    }

    // ==================== РАСПИСАНИЕ ====================

    [HttpGet("children/{studentId:int}/schedule")]
    public async Task<IActionResult> GetSchedule(int studentId, [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null)
            return Ok(new { quarter = (object?)null, lessons = Array.Empty<object>() });

        Quarter? quarter = quarterId.HasValue
            ? await _context.Quarters.FindAsync(quarterId.Value)
            : await GetCurrentQuarterAsync();

        if (quarter == null)
            return Ok(new { quarter = (object?)null, lessons = Array.Empty<object>() });

        var schedule = await _context.Schedules
            .AsNoTracking()
            .Include(sc => sc.Subject)
            .Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Where(sc => sc.ClassId == student.ClassId && sc.QuarterId == quarter.Id)
            .OrderBy(sc => sc.DayOfWeek)
            .ThenBy(sc => sc.LessonNumber)
            .Select(sc => new
            {
                sc.Id,
                sc.DayOfWeek,
                sc.LessonNumber,
                sc.Cabinet,
                SubjectId = sc.SubjectId,
                SubjectName = sc.Subject != null ? sc.Subject.Name : null,
                TeacherName = sc.Teacher != null && sc.Teacher.User != null
                    ? sc.Teacher.User.FullName : null
            })
            .ToListAsync();

        return Ok(new
        {
            quarter = new
            {
                quarter.Id,
                quarter.AcademicYear,
                quarter.QuarterNumber,
                quarter.DateStart,
                quarter.DateEnd,
                Name = $"{quarter.QuarterNumber}-я четверть",
                YearLabel = $"{quarter.AcademicYear}/{quarter.AcademicYear + 1}"
            },
            lessons = schedule
        });
    }

    // ==================== АНАЛИТИКА ====================

    [HttpGet("children/{studentId:int}/analytics")]
    public async Task<IActionResult> Analytics(int studentId,
                                               [FromQuery] DateTime? from,
                                               [FromQuery] DateTime? to,
                                               [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        (from, to) = await ResolveRangeAsync(quarterId, from, to);

        var gradesQ = _context.Grades.Where(g => g.StudentId == studentId);
        var attQ = _context.Attendances.Where(a => a.StudentId == studentId);
        var hwQ = _context.HomeworkStatuses.Where(h => h.StudentId == studentId);

        if (from.HasValue)
        {
            gradesQ = gradesQ.Where(g => g.Date >= from);
            attQ = attQ.Where(a => a.Date >= from);
        }
        if (to.HasValue)
        {
            gradesQ = gradesQ.Where(g => g.Date <= to);
            attQ = attQ.Where(a => a.Date <= to);
        }

        var grades = await gradesQ.ToListAsync();
        var attendance = await attQ.ToListAsync();
        var hwStatuses = await hwQ.ToListAsync();

        var student = await _context.Students.FindAsync(studentId);
        double classAvg = 0;
        if (student?.ClassId != null)
        {
            var classGradesQ = _context.Grades.Where(g => g.Student!.ClassId == student.ClassId);
            if (from.HasValue) classGradesQ = classGradesQ.Where(g => g.Date >= from);
            if (to.HasValue) classGradesQ = classGradesQ.Where(g => g.Date <= to);

            var classGrades = await classGradesQ.ToListAsync();
            if (classGrades.Any()) classAvg = classGrades.Average(g => g.Value);
        }

        var myAvg = grades.Any() ? grades.Average(g => g.Value) : 0;

        var bySubject = grades
            .GroupBy(g => g.SubjectId)
            .Select(g => new
            {
                SubjectId = g.Key,
                Average = g.Average(x => x.Value),
                Count = g.Count()
            })
            .ToList();

        return Ok(new
        {
            averageGrade = myAvg,
            classAverage = classAvg,
            deviation = myAvg - classAvg,
            attendanceRate = attendance.Any()
                ? (double)attendance.Count(a => a.Status != "н" && a.Status != "отсутствовал") / attendance.Count * 100
                : 100,
            absences = attendance.Count(a => a.Status == "н" || a.Status == "отсутствовал"),
            homeworkTotal = hwStatuses.Count,
            homeworkDone = hwStatuses.Count(h => h.Status == "выполнено" || h.Status == "done"),
            bySubject
        });
    }

    // ==================== УВЕДОМЛЕНИЯ ====================

    [HttpGet("children/{studentId:int}/notifications")]
    public async Task<IActionResult> GetNotifications(int studentId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var notifications = new List<object>();

        var recentGrades = await _context.Grades
            .Include(g => g.Subject)
            .Where(g => g.StudentId == studentId && g.Date >= DateTime.UtcNow.AddDays(-7))
            .OrderByDescending(g => g.Date)
            .ToListAsync();

        foreach (var g in recentGrades)
        {
            notifications.Add(new
            {
                Type = "grade",
                Message = $"Новая оценка по {g.Subject?.Name}: {g.Value}",
                Date = g.Date
            });
        }

        var recentAbsences = await _context.Attendances
            .Include(a => a.Subject)
            .Where(a => a.StudentId == studentId
                     && a.Date >= DateTime.UtcNow.AddDays(-7)
                     && (a.Status == "н" || a.Status == "отсутствовал"))
            .ToListAsync();

        foreach (var a in recentAbsences)
        {
            notifications.Add(new
            {
                Type = "absence",
                Message = $"Пропуск по {a.Subject?.Name}",
                Date = a.Date
            });
        }

        var missedHw = await _context.Homeworks
            .Include(h => h.Subject)
            .Where(h => h.ClassId == _context.Students
                .Where(s => s.Id == studentId)
                .Select(s => s.ClassId).FirstOrDefault()
                && h.DateDue < DateTime.UtcNow
                && !h.HomeworkStatuses.Any(hs => hs.StudentId == studentId
                    && (hs.Status == "выполнено" || hs.Status == "done")))
            .ToListAsync();

        foreach (var h in missedHw)
        {
            notifications.Add(new
            {
                Type = "homework",
                Message = $"Не сдано ДЗ по {h.Subject?.Name}",
                Date = h.DateDue
            });
        }

        return Ok(notifications);
    }

    // ==================== НЕДЕЛЯ ====================

    [HttpGet("children/{studentId:int}/week")]
    public async Task<IActionResult> GetWeek(int studentId, [FromQuery] DateTime? startDate)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null)
            return Ok(new { startDate = DateTime.UtcNow, days = Array.Empty<object>() });

        var baseDate = (startDate ?? DateTime.UtcNow).Date;
        while (baseDate.DayOfWeek != DayOfWeek.Monday) baseDate = baseDate.AddDays(-1);
        var toExclusive = baseDate.AddDays(7);

        // четверть, покрывающая начало недели
        var quarter = await GetQuarterForDateAsync(baseDate);

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда",
                             "Четверг", "Пятница", "Суббота" };

        var schedules = quarter == null
            ? new List<Schedule>()
            : await _context.Schedules
                .Include(sc => sc.Subject).Include(sc => sc.Teacher).ThenInclude(t => t!.User)
                .Where(sc => sc.ClassId == student.ClassId && sc.QuarterId == quarter.Id)
                .ToListAsync();

        var homeworks = await _context.Homeworks
            .Where(h => h.ClassId == student.ClassId
                     && ((h.DateIssued >= baseDate && h.DateIssued < toExclusive) ||
                         (h.DateDue >= baseDate && h.DateDue < toExclusive)))
            .ToListAsync();
        var hwIds = homeworks.Select(h => h.Id).ToList();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hs.StudentId == studentId && hwIds.Contains(hs.HomeworkId ?? 0)).ToListAsync();

        var grades = await _context.Grades
            .Where(g => g.StudentId == studentId
                     && g.Date >= baseDate && g.Date < toExclusive).ToListAsync();

        var atts = await _context.Attendances
            .Where(a => a.StudentId == studentId
                     && a.Date >= baseDate && a.Date < toExclusive).ToListAsync();

        var days = new List<object>();
        for (int i = 0; i < 6; i++)
        {
            var date = baseDate.AddDays(i);
            var dow = i + 1;
            var daySchedules = schedules.Where(sc => sc.DayOfWeek == dow)
                .OrderBy(sc => sc.LessonNumber).ToList();

            var lessons = new List<object>();
            foreach (var sc in daySchedules)
            {
                var hw = homeworks.FirstOrDefault(h => h.SubjectId == sc.SubjectId
                    && (h.DateIssued.Date == date || h.DateDue.Date == date));

                HomeworkStatus? hwStatus = null;
                if (hw != null)
                    hwStatus = hwStatuses.FirstOrDefault(x => x.HomeworkId == hw.Id);

                var att = atts.FirstOrDefault(a => a.SubjectId == sc.SubjectId && a.Date.Date == date);
                var lessonGrades = grades
                    .Where(g => g.SubjectId == sc.SubjectId && g.Date.Date == date)
                    .Select(g => new { g.Value, g.Type, g.Comment }).ToList();

                lessons.Add(new
                {
                    lessonNumber = sc.LessonNumber,
                    subjectId = sc.SubjectId,
                    subjectName = sc.Subject?.Name,
                    teacherName = sc.Teacher?.User?.FullName,
                    cabinet = sc.Cabinet,
                    homework = hw == null ? null : new
                    {
                        id = hw.Id,
                        description = hw.Description,
                        dateIssued = hw.DateIssued,
                        dateDue = hw.DateDue,
                        status = hwStatus?.Status,
                        dateSubmitted = hwStatus?.DateSubmitted
                    },
                    attendance = att == null ? null : new { status = att.Status, comment = att.Comment },
                    grades = lessonGrades
                });
            }
            days.Add(new { date, dayOfWeek = dow, dayName = dayNames[dow], lessons });
        }

        return Ok(new
        {
            startDate = baseDate,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            days
        });
    }
}