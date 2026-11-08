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
        return await _context.Parents.FirstOrDefaultAsync(x => x.UserId == uid);
    }

    private async Task<bool> HasAccessToAsync(int parentId, int studentId)
    {
        return await _context.ParentStudents.AnyAsync(ps =>
            ps.ParentId == parentId && ps.StudentId == studentId);
    }

    private static DateTime ToDateTime(DateOnly d) => new DateTime(d.Year, d.Month, d.Day);
    private static DateTime ToEndOfDay(DateOnly d) => new DateTime(d.Year, d.Month, d.Day, 23, 59, 59);

    private static DateTime MondayOf(DateTime d)
    {
        var x = d.Date;
        while (x.DayOfWeek != DayOfWeek.Monday) x = x.AddDays(-1);
        return x;
    }

    private static bool IsAbsent(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return false;
        var s = status.Trim().ToLower();
        return s == "н" || s == "отсутствовал" || s == "отсутствовала";
    }

    private async Task<Quarter?> GetQuarterForDateAsync(DateTime date)
    {
        var d = DateOnly.FromDateTime(date);
        return await _context.Quarters
            .Where(q => q.DateStart <= d && q.DateEnd >= d)
            .OrderBy(q => q.QuarterNumber)
            .FirstOrDefaultAsync();
    }

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

        return Ok(children);
    }

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

    [HttpGet("children/{studentId:int}/subjects")]
    public async Task<IActionResult> GetSubjects(int studentId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null) return Ok(Array.Empty<object>());

        var subjects = await _context.Schedules
            .Where(sc => sc.ClassId == student.ClassId)
            .Include(sc => sc.Subject)
            .Select(sc => new { sc.SubjectId, Name = sc.Subject!.Name })
            .Distinct()
            .ToListAsync();

        return Ok(subjects);
    }

    [HttpGet("children/{studentId:int}/week")]
    public async Task<IActionResult> GetWeek(int studentId, [FromQuery] DateTime? startDate, [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null)
            return Ok(new { startDate = DateTime.UtcNow, days = Array.Empty<object>(), quarterId = (int?)null });

        Quarter? quarter = null;
        if (quarterId.HasValue)
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
        if (quarter == null)
            quarter = await GetQuarterForDateAsync(startDate ?? DateTime.UtcNow);

        if (quarter == null)
            return Ok(new { startDate = DateTime.UtcNow, days = Array.Empty<object>(), quarterId = (int?)null });

        var minMon = MondayOf(ToDateTime(quarter.DateStart));
        var maxMon = MondayOf(ToDateTime(quarter.DateEnd));

        var baseDate = MondayOf(startDate ?? minMon);
        if (baseDate < minMon) baseDate = minMon;
        if (baseDate > maxMon) baseDate = maxMon;

        return Ok(await BuildWeekAsync(studentId, student.ClassId.Value, baseDate, quarter));
    }

    private async Task<object> BuildWeekAsync(int studentId, int classId, DateTime baseDate, Quarter quarter)
    {
        var toExclusive = baseDate.AddDays(7);
        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };

        var schedules = await _context.Schedules
            .Include(sc => sc.Subject).Include(sc => sc.Teacher).ThenInclude(t => t!.User)
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
            .Where(g => g.StudentId == studentId && g.Date >= baseDate && g.Date < toExclusive)
            .ToListAsync();

        var atts = await _context.Attendances
            .Where(a => a.StudentId == studentId && a.Date >= baseDate && a.Date < toExclusive)
            .ToListAsync();

        var qStart = ToDateTime(quarter.DateStart);
        var qEnd = ToDateTime(quarter.DateEnd);

        var days = new List<object>();
        for (int i = 0; i < 6; i++)
        {
            var date = baseDate.AddDays(i);
            var dow = i + 1;
            bool inQuarter = date >= qStart && date <= qEnd;

            var lessons = new List<object>();
            if (inQuarter)
            {
                var daySchedules = schedules
                    .Where(sc => sc.DayOfWeek == dow)
                    .OrderBy(sc => sc.LessonNumber)
                    .ToList();

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
            }
            days.Add(new { date, dayOfWeek = dow, dayName = dayNames[dow], lessons, inQuarter });
        }

        return new
        {
            startDate = baseDate,
            quarterId = quarter.Id,
            quarterName = $"{quarter.QuarterNumber}-я четверть",
            quarterStart = quarter.DateStart,
            quarterEnd = quarter.DateEnd,
            days
        };
    }

    [HttpGet("children/{studentId:int}/journal")]
    public async Task<IActionResult> GetJournal(int studentId, [FromQuery] int subjectId, [FromQuery] int quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null)
            return Ok(new { dates = Array.Empty<object>(), cells = Array.Empty<object>(), subjectName = "" });

        var quarter = await _context.Quarters.FindAsync(quarterId);
        if (quarter == null)
            return Ok(new { dates = Array.Empty<object>(), cells = Array.Empty<object>(), subjectName = "" });

        var fromDate = ToDateTime(quarter.DateStart);
        var toDate = ToDateTime(quarter.DateEnd);
        var toExclusive = toDate.AddDays(1);

        var weekdays = await _context.Schedules
            .Where(sc => sc.ClassId == student.ClassId && sc.SubjectId == subjectId && sc.QuarterId == quarterId)
            .Select(sc => sc.DayOfWeek)
            .Distinct()
            .ToListAsync();

        var dates = new List<DateTime>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (weekdays.Contains((int)d.DayOfWeek)) dates.Add(d);
        }

        var grades = await _context.Grades
            .Where(g => g.StudentId == studentId && g.SubjectId == subjectId
                     && g.Date >= fromDate && g.Date < toExclusive)
            .ToListAsync();

        var atts = await _context.Attendances
            .Where(a => a.StudentId == studentId && a.SubjectId == subjectId
                     && a.Date >= fromDate && a.Date < toExclusive)
            .ToListAsync();

        var cells = dates.Select(dt =>
        {
            var g = grades.FirstOrDefault(x => x.Date.Date == dt);
            var a = atts.FirstOrDefault(x => x.Date.Date == dt);
            return new
            {
                date = dt,
                gradeId = g?.Id,
                grade = g == null ? (int?)null : g.Value,
                gradeType = g?.Type,
                gradeComment = g?.Comment,
                attendanceId = a?.Id,
                attendance = a?.Status,
                attendanceComment = a?.Comment
            };
        }).ToList();

        var subjectName = await _context.Subjects
            .Where(x => x.Id == subjectId)
            .Select(x => x.Name)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            subjectId,
            subjectName,
            quarterId = quarter.Id,
            quarterName = $"{quarter.QuarterNumber}-я четверть",
            quarterStart = quarter.DateStart,
            quarterEnd = quarter.DateEnd,
            dates,
            cells
        });
    }

    [HttpGet("children/{studentId:int}/summary")]
    public async Task<IActionResult> GetSummary(int studentId, [FromQuery] int quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var student = await _context.Students.FindAsync(studentId);
        if (student?.ClassId == null) return Ok(Array.Empty<object>());

        var quarter = await _context.Quarters.FindAsync(quarterId);
        if (quarter == null) return Ok(Array.Empty<object>());

        var fromDate = ToDateTime(quarter.DateStart);
        var toDate = ToDateTime(quarter.DateEnd);
        var toExclusive = toDate.AddDays(1);

        var yearStart = ToDateTime(new DateOnly(quarter.AcademicYear, 9, 1));
        var yearEnd = ToDateTime(new DateOnly(quarter.AcademicYear + 1, 6, 30));

        var subjects = await _context.Schedules
            .Where(sc => sc.ClassId == student.ClassId)
            .Include(sc => sc.Subject)
            .Select(sc => new { sc.SubjectId, Name = sc.Subject!.Name })
            .Distinct()
            .ToListAsync();

        var result = new List<object>();

        foreach (var subj in subjects)
        {
            if (subj.SubjectId == null) continue;

            var weekdays = await _context.Schedules
                .Where(sc => sc.ClassId == student.ClassId && sc.SubjectId == subj.SubjectId && sc.QuarterId == quarterId)
                .Select(sc => sc.DayOfWeek)
                .Distinct()
                .ToListAsync();

            int lessonsCount = 0;
            for (var d = fromDate; d <= toDate; d = d.AddDays(1))
            {
                if (d.DayOfWeek == DayOfWeek.Sunday) continue;
                if (weekdays.Contains((int)d.DayOfWeek)) lessonsCount++;
            }

            var grades = await _context.Grades
                .Where(g => g.StudentId == studentId && g.SubjectId == subj.SubjectId
                         && g.Date >= fromDate && g.Date < toExclusive)
                .ToListAsync();

            var atts = await _context.Attendances
                .Where(a => a.StudentId == studentId && a.SubjectId == subj.SubjectId
                         && a.Date >= fromDate && a.Date < toExclusive)
                .ToListAsync();

            var avgQ = grades.Any() ? Math.Round(grades.Average(g => g.Value), 2) : (double?)null;
            var attended = atts.Count(a => !IsAbsent(a.Status));
            var totalAtt = lessonsCount > 0 ? lessonsCount : atts.Count;
            var attRate = totalAtt > 0 ? Math.Round((double)attended / totalAtt * 100, 0) : (double?)null;

            var yearGrades = await _context.Grades
                .Where(g => g.StudentId == studentId && g.SubjectId == subj.SubjectId
                         && g.Date >= yearStart && g.Date <= yearEnd)
                .ToListAsync();
            var yearAvg = yearGrades.Any() ? Math.Round(yearGrades.Average(g => g.Value), 2) : (double?)null;

            result.Add(new
            {
                subjectId = subj.SubjectId,
                subjectName = subj.Name,
                quarterAvg = avgQ,
                gradesCount = grades.Count,
                attendanceRate = attRate,
                attended,
                totalLessons = totalAtt,
                yearAvg
            });
        }

        return Ok(result);
    }

    [HttpGet("children/{studentId:int}/analytics")]
    public async Task<IActionResult> Analytics(int studentId, [FromQuery] int? quarterId)
    {
        var p = await GetCurrentParentAsync();
        if (p == null) return NotFound();
        if (!await HasAccessToAsync(p.Id, studentId)) return Forbid();

        var s = await _context.Students.FindAsync(studentId);
        if (s == null) return NotFound();

        DateTime? from = null, to = null;
        if (quarterId.HasValue)
        {
            var q = await _context.Quarters.FindAsync(quarterId.Value);
            if (q != null)
            {
                from = ToDateTime(q.DateStart);
                to = ToEndOfDay(q.DateEnd);
            }
        }

        var gradesQ = _context.Grades.Where(g => g.StudentId == studentId);
        var attQ = _context.Attendances.Where(a => a.StudentId == studentId);

        if (from.HasValue) { gradesQ = gradesQ.Where(g => g.Date >= from); attQ = attQ.Where(a => a.Date >= from); }
        if (to.HasValue) { gradesQ = gradesQ.Where(g => g.Date <= to); attQ = attQ.Where(a => a.Date <= to); }

        var grades = await gradesQ.ToListAsync();
        var attendance = await attQ.ToListAsync();

        var hwQuery = _context.Homeworks.Where(h => h.ClassId == s.ClassId);
        if (from.HasValue) hwQuery = hwQuery.Where(h => h.DateIssued >= from);
        if (to.HasValue) hwQuery = hwQuery.Where(h => h.DateIssued <= to);
        var hwIds = await hwQuery.Select(h => h.Id).ToListAsync();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hs.StudentId == studentId && hwIds.Contains(hs.HomeworkId ?? 0))
            .ToListAsync();

        var doneHw = hwStatuses.Count(hs =>
        {
            var st = (hs.Status ?? "").Trim().ToLower();
            return st == "выполнено" || st == "сдано" || st == "готово" || st == "done";
        });

        var subjectNames = await _context.Subjects.ToDictionaryAsync(x => x.Id, x => x.Name);

        var bySubject = grades
            .GroupBy(g => g.SubjectId)
            .Select(g => new
            {
                subjectId = g.Key,
                subjectName = g.Key.HasValue && subjectNames.ContainsKey(g.Key.Value) ? subjectNames[g.Key.Value] : "",
                average = Math.Round(g.Average(x => x.Value), 2),
                count = g.Count()
            })
            .OrderBy(x => x.subjectName)
            .ToList();

        var gradeDistribution = new[]
        {
            new { label = "9–10", count = grades.Count(g => g.Value >= 9), color = "#137a3d" },
            new { label = "7–8",  count = grades.Count(g => g.Value >= 7 && g.Value <= 8), color = "#4a90e2" },
            new { label = "4–6",  count = grades.Count(g => g.Value >= 4 && g.Value <= 6), color = "#f5a623" },
            new { label = "1–3",  count = grades.Count(g => g.Value >= 1 && g.Value <= 3), color = "#d0021b" }
        };

        int present = 0, absent = 0, valid = 0;
        foreach (var a in attendance)
        {
            var st = (a.Status ?? "").Trim().ToLower();
            if (st == "н" || st == "отсутствовал" || st == "отсутствовала") absent++;
            else if (st == "уважительная") valid++;
            else present++;
        }

        var attDist = new[]
        {
            new { label = "Присутствовал", count = present, color = "#4a90e2" },
            new { label = "Отсутствовал",  count = absent,  color = "#d0021b" },
            new { label = "Уважительная",  count = valid,   color = "#f5a623" }
        };

        var hwDist = new[]
        {
            new { label = "Сдано",     count = doneHw,                color = "#4a90e2" },
            new { label = "Не сдано",  count = hwIds.Count - doneHw,  color = "#d0021b" }
        };

        var monthsRu = new[] { "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
                               "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
        var now = DateTime.UtcNow;
        var dynamics = new List<object>();
        for (int i = 5; i >= 0; i--)
        {
            var m = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
            var next = m.AddMonths(1);
            var mg = grades.Where(g => g.Date >= m && g.Date < next).ToList();
            dynamics.Add(new
            {
                label = monthsRu[m.Month - 1],
                average = mg.Any() ? Math.Round(mg.Average(g => g.Value), 2) : 0,
                count = mg.Count
            });
        }

        return Ok(new
        {
            averageGrade = grades.Any() ? Math.Round(grades.Average(g => g.Value), 2) : 0,
            totalGrades = grades.Count,
            attendanceRate = attendance.Any()
                ? Math.Round((double)(attendance.Count - absent) / attendance.Count * 100, 1)
                : 100.0,
            homeworkCompletion = hwIds.Count > 0
                ? Math.Round((double)doneHw / hwIds.Count * 100, 0)
                : 0,
            absences = absent,
            bySubject,
            gradeDistribution,
            attendanceDistribution = attDist,
            homeworkDistribution = hwDist,
            monthlyDynamics = dynamics
        });
    }
}