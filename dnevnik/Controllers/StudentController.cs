using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dnevnik.Data;
using dnevnik.Entities;

namespace dnevnik.Controllers;

[ApiController]
[Route("api/student")]
[Authorize(Roles = "Ученик,Учащийся,Student")]
public class StudentController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<StudentController> _logger;

    public StudentController(ApplicationDbContext context, ILogger<StudentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst("userId")!.Value);

    private async Task<Student?> GetCurrentStudentAsync()
    {
        var uid = GetUserId();
        var s = await _context.Students
            .Include(x => x.Class)
            .FirstOrDefaultAsync(x => x.UserId == uid);
        if (s == null) _logger.LogWarning("Профиль ученика не найден для UserId={UserId}", uid);
        return s;
    }

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


    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound(new { message = "Профиль ученика не найден" });

        return Ok(new
        {
            s.Id,
            ClassId = s.ClassId,
            ClassName = s.Class?.Name,
            s.BirthDate
        });
    }

    // ==================== ЧЕТВЕРТИ ====================

    [HttpGet("quarters")]
    public async Task<IActionResult> GetQuarters()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var items = await _context.Quarters
            .AsNoTracking()
            .OrderByDescending(q => q.AcademicYear)
            .ThenBy(q => q.QuarterNumber)
            .Select(q => new
            {
                q.Id,
                q.AcademicYear,
                q.QuarterNumber,
                q.DateStart,
                q.DateEnd,
                IsCurrent = q.DateStart <= today && q.DateEnd >= today
            })
            .ToListAsync();

        // для отображения
        var result = items.Select(q => new
        {
            q.Id,
            q.AcademicYear,
            q.QuarterNumber,
            q.DateStart,
            q.DateEnd,
            q.IsCurrent,
            YearLabel = $"{q.AcademicYear}/{q.AcademicYear + 1}",
            Name = $"{q.QuarterNumber}-я четверть",
            FullName = $"{q.QuarterNumber}-я четверть ({q.AcademicYear}/{q.AcademicYear + 1})"
        });

        return Ok(result);
    }

    // ==================== РАСПИСАНИЕ ====================

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule([FromQuery] int? quarterId)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound(new { message = "Профиль ученика не найден" });
        if (s.ClassId == null) return Ok(Array.Empty<object>());

        Quarter? quarter = null;
        if (quarterId.HasValue)
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
        else
            quarter = await GetCurrentQuarterAsync();

        if (quarter == null) return Ok(Array.Empty<object>());

        var schedule = await _context.Schedules
            .AsNoTracking()
            .Include(sc => sc.Subject)
            .Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Include(sc => sc.Quarter)
            .Where(sc => sc.ClassId == s.ClassId && sc.QuarterId == quarter.Id)
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
                TeacherId = sc.TeacherId,
                TeacherName = sc.Teacher != null && sc.Teacher.User != null
                    ? sc.Teacher.User.FullName : null,
                QuarterId = sc.QuarterId,
                QuarterName = sc.Quarter.QuarterNumber + "-я четверть",
                sc.Quarter.AcademicYear
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

    // ==================== ОЦЕНКИ ====================

    [HttpGet("grades")]
    public async Task<IActionResult> GetGrades([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                               [FromQuery] int? quarterId)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound();

        var q = _context.Grades
            .Include(g => g.Subject)
            .Include(g => g.Teacher).ThenInclude(t => t!.User)
            .Where(g => g.StudentId == s.Id);

        (from, to) = await ResolveRangeAsync(quarterId, from, to);
        if (from.HasValue) q = q.Where(g => g.Date >= from);
        if (to.HasValue) q = q.Where(g => g.Date <= to);

        var result = await q.OrderByDescending(g => g.Date).ToListAsync();
        return Ok(result);
    }

    // ==================== ДОМАШКА ====================

    [HttpGet("homework")]
    public async Task<IActionResult> GetHomework([FromQuery] int? quarterId)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound();

        var hwQuery = _context.Homeworks
            .Include(h => h.Subject)
            .Where(h => h.ClassId == s.ClassId);

        var (from, to) = await ResolveRangeAsync(quarterId, null, null);
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
                    .Where(hs => hs.StudentId == s.Id)
                    .Select(hs => hs.Status).FirstOrDefault(),
                DateSubmitted = h.HomeworkStatuses
                    .Where(hs => hs.StudentId == s.Id)
                    .Select(hs => hs.DateSubmitted).FirstOrDefault()
            })
            .ToListAsync();

        return Ok(hw);
    }

    // ==================== ПОСЕЩАЕМОСТЬ ====================

    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                                   [FromQuery] int? quarterId)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound();

        var q = _context.Attendances
            .Include(a => a.Subject)
            .Where(a => a.StudentId == s.Id);

        (from, to) = await ResolveRangeAsync(quarterId, from, to);
        if (from.HasValue) q = q.Where(a => a.Date >= from);
        if (to.HasValue) q = q.Where(a => a.Date <= to);

        var result = await q.OrderByDescending(a => a.Date).ToListAsync();
        return Ok(result);
    }

    // ==================== АНАЛИТИКА ====================

    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics([FromQuery] DateTime? from, [FromQuery] DateTime? to,
                                               [FromQuery] int? quarterId)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound();

        (from, to) = await ResolveRangeAsync(quarterId, from, to);

        var gradesQ = _context.Grades.Where(g => g.StudentId == s.Id);
        var attQ = _context.Attendances.Where(a => a.StudentId == s.Id);
        var hwQ = _context.HomeworkStatuses.Where(h => h.StudentId == s.Id);

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
            averageGrade = grades.Any() ? grades.Average(g => g.Value) : 0,
            totalGrades = grades.Count,
            attendanceRate = attendance.Any()
                ? (double)attendance.Count(a => a.Status != "н" && a.Status != "отсутствовал") / attendance.Count * 100
                : 100,
            absences = attendance.Count(a => a.Status == "н" || a.Status == "отсутствовал"),
            homeworkTotal = hwStatuses.Count,
            homeworkDone = hwStatuses.Count(h => h.Status == "выполнено" || h.Status == "done"),
            bySubject,
            from,
            to
        });
    }

    // ==================== НЕДЕЛЯ ====================

    [HttpGet("week")]
    public async Task<IActionResult> GetWeek([FromQuery] DateTime? startDate)
    {
        var s = await GetCurrentStudentAsync();
        if (s == null) return NotFound(new { message = "Профиль ученика не найден" });
        if (s.ClassId == null) return Ok(new { startDate = DateTime.UtcNow, days = Array.Empty<object>() });

        return Ok(await BuildWeekAsync(s.Id, s.ClassId.Value, startDate));
    }

    internal async Task<object> BuildWeekAsync(int studentId, int classId, DateTime? startDate)
    {
        var baseDate = (startDate ?? DateTime.UtcNow).Date;
        while (baseDate.DayOfWeek != DayOfWeek.Monday) baseDate = baseDate.AddDays(-1);
        var toExclusive = baseDate.AddDays(7);

        // четверть, покрывающая понедельник недели
        var quarter = await GetQuarterForDateAsync(baseDate);

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда",
                             "Четверг", "Пятница", "Суббота" };

        // расписание только этой четверти
        var schedules = quarter == null
            ? new List<Schedule>()
            : await _context.Schedules
                .Include(sc => sc.Subject)
                .Include(sc => sc.Teacher).ThenInclude(t => t!.User)
                .Where(sc => sc.ClassId == classId && sc.QuarterId == quarter.Id)
                .ToListAsync();

        var homeworks = await _context.Homeworks
            .Where(h => h.ClassId == classId
                     && ((h.DateIssued >= baseDate && h.DateIssued < toExclusive) ||
                         (h.DateDue >= baseDate && h.DateDue < toExclusive)))
            .ToListAsync();
        var hwIds = homeworks.Select(h => h.Id).ToList();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hs.StudentId == studentId && hwIds.Contains(hs.HomeworkId ?? 0))
            .ToListAsync();

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
            var daySchedules = schedules
                .Where(sc => sc.DayOfWeek == dow)
                .OrderBy(sc => sc.LessonNumber)
                .ToList();

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
                    .Select(g => new { g.Value, g.Type, g.Comment })
                    .ToList();

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

        return new
        {
            startDate = baseDate,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            days
        };
    }
    private async Task<(DateTime? from, DateTime? to)> ResolveRangeAsync(int? quarterId,
                                                                        DateTime? from, DateTime? to)
    {
        if (quarterId.HasValue)
        {
            var q = await _context.Quarters.FindAsync(quarterId.Value);
            if (q != null)
            {
                from = q.DateStart.ToDateTime(TimeOnly.MinValue);
                to = q.DateEnd.ToDateTime(TimeOnly.MaxValue);
            }
        }
        return (from, to);
    }
}