using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dnevnik.Data;
using dnevnik.Entities;
using System.Text;

namespace dnevnik.Controllers;

[ApiController]
[Route("api/teacher")]
[Authorize(Roles = "Учитель,Teacher")]
public class TeacherController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<TeacherController> _logger;

    public TeacherController(ApplicationDbContext context, ILogger<TeacherController> logger)
    {
        _context = context;
        _logger = logger;
    }

    private int GetUserId() => int.Parse(User.FindFirst("userId")!.Value);

    private async Task<Teacher?> GetCurrentTeacherAsync()
    {
        var uid = GetUserId();
        var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == uid);
        if (teacher == null)
            _logger.LogWarning("Профиль учителя не найден для UserId={UserId}", uid);
        return teacher;
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

    private static DateTime ToDateTime(DateOnly d) => new DateTime(d.Year, d.Month, d.Day);

    private static DateTime ToEndOfDay(DateOnly d) => new DateTime(d.Year, d.Month, d.Day, 23, 59, 59);

    private static DateTime MondayOf(DateTime date)
    {
        var d = date.Date;
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(-1);
        return d;
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

    [HttpGet("my-classes")]
    public async Task<IActionResult> GetMyClasses()
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound(new { message = "Профиль учителя не найден" });

        var data = await _context.TeacherSubjects
            .Where(ts => ts.TeacherId == teacher.Id)
            .Include(ts => ts.Class)
            .Include(ts => ts.Subject)
            .Select(ts => new
            {
                ts.Id,
                ClassId = ts.ClassId,
                ClassName = ts.Class!.Name,
                SubjectId = ts.SubjectId,
                SubjectName = ts.Subject!.Name
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpGet("diary")]
    public async Task<IActionResult> GetDiary([FromQuery] int classId,
                                              [FromQuery] int? subjectId,
                                              [FromQuery] int? studentId,
                                              [FromQuery] DateTime? weekStart)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();

        var linked = await _context.TeacherSubjects.AnyAsync(ts =>
            ts.TeacherId == teacher.Id && ts.ClassId == classId);
        if (!linked) return Forbid();

        return Ok(await BuildDiaryAsync(classId, subjectId, studentId, weekStart));
    }

    internal async Task<object> BuildDiaryAsync(int classId, int? subjectId,
                                                int? studentId, DateTime? weekStart)
    {
        var baseDate = MondayOf(weekStart ?? DateTime.UtcNow);
        var toExclusive = baseDate.AddDays(7);

        var quarter = await GetQuarterForDateAsync(baseDate);

        var studentsQuery = _context.Students
            .Include(s => s.User)
            .Where(s => s.ClassId == classId);
        if (studentId.HasValue) studentsQuery = studentsQuery.Where(s => s.Id == studentId);

        var students = await studentsQuery
            .OrderBy(s => s.User!.FullName)
            .Select(s => new { s.Id, Name = s.User!.FullName })
            .ToListAsync();

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда",
                             "Четверг", "Пятница", "Суббота" };

        var scheduleQuery = _context.Schedules
            .Include(sc => sc.Subject)
            .Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Where(sc => sc.ClassId == classId);

        if (quarter != null)
            scheduleQuery = scheduleQuery.Where(sc => sc.QuarterId == quarter.Id);
        if (subjectId.HasValue)
            scheduleQuery = scheduleQuery.Where(sc => sc.SubjectId == subjectId);

        var schedules = await scheduleQuery.ToListAsync();

        var hwQuery = _context.Homeworks
            .Where(h => h.ClassId == classId
                     && h.DateIssued >= baseDate && h.DateIssued < toExclusive);
        if (subjectId.HasValue)
            hwQuery = hwQuery.Where(h => h.SubjectId == subjectId);
        var homeworks = await hwQuery.ToListAsync();
        var hwIds = homeworks.Select(h => h.Id).ToList();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hwIds.Contains(hs.HomeworkId ?? 0))
            .ToListAsync();

        var gradesQuery = _context.Grades
            .Where(g => g.Student!.ClassId == classId
                     && g.Date >= baseDate && g.Date < toExclusive);
        if (subjectId.HasValue)
            gradesQuery = gradesQuery.Where(g => g.SubjectId == subjectId);
        var grades = await gradesQuery.ToListAsync();

        var attQuery = _context.Attendances
            .Where(a => a.Student!.ClassId == classId
                     && a.Date >= baseDate && a.Date < toExclusive);
        if (subjectId.HasValue)
            attQuery = attQuery.Where(a => a.SubjectId == subjectId);
        var attendances = await attQuery.ToListAsync();

        var diaries = new List<object>();
        foreach (var st in students)
        {
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
                        hwStatus = hwStatuses.FirstOrDefault(x =>
                            x.HomeworkId == hw.Id && x.StudentId == st.Id);

                    var att = attendances.FirstOrDefault(a =>
                        a.StudentId == st.Id && a.SubjectId == sc.SubjectId
                        && a.Date.Date == date);

                    var lessonGrades = grades
                        .Where(g => g.StudentId == st.Id
                                 && g.SubjectId == sc.SubjectId
                                 && g.Date.Date == date)
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
            diaries.Add(new { studentId = st.Id, studentName = st.Name, days });
        }

        return new
        {
            classId,
            subjectId,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            weekStart = baseDate,
            weekEnd = baseDate.AddDays(5),
            diaries
        };
    }


    [HttpGet("homework-diary")]
    public async Task<IActionResult> GetHomeworkDiary([FromQuery] int classId, [FromQuery] DateTime? weekStart, [FromQuery] int? quarterId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();

        var teacherSubjectIds = await _context.TeacherSubjects
            .Where(ts => ts.TeacherId == teacher.Id && ts.ClassId == classId)
            .Select(ts => ts.SubjectId)
            .ToListAsync();

        if (!teacherSubjectIds.Any()) return Forbid();

        Quarter? quarter = null;
        if (quarterId.HasValue)
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
        else
            quarter = await GetQuarterForDateAsync((weekStart ?? DateTime.UtcNow).Date);

        var baseDate = MondayOf(weekStart ?? DateTime.UtcNow);

        DateTime? qStartDt = null, qEndDt = null;
        if (quarter != null)
        {
            qStartDt = ToDateTime(quarter.DateStart);
            qEndDt = ToDateTime(quarter.DateEnd);

            var minMon = MondayOf(qStartDt.Value);
            var maxMon = MondayOf(qEndDt.Value);
            if (baseDate < minMon) baseDate = minMon;
            if (baseDate > maxMon) baseDate = maxMon;
        }

        var toExclusive = baseDate.AddDays(7);

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда",
                         "Четверг", "Пятница", "Суббота" };

        var schedulesQuery = _context.Schedules
            .Include(sc => sc.Subject).Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Where(sc => sc.ClassId == classId && sc.SubjectId.HasValue && teacherSubjectIds.Contains(sc.SubjectId.Value));

        if (quarter != null)
            schedulesQuery = schedulesQuery.Where(sc => sc.QuarterId == quarter.Id);

        var schedules = await schedulesQuery.ToListAsync();

        var weekdaysBySubject = schedules
            .Where(sc => sc.SubjectId.HasValue)
            .GroupBy(sc => sc.SubjectId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(sc => sc.DayOfWeek).Distinct().ToHashSet()
            );

        DateTime? PrevLessonDate(int subjectId, DateTime beforeDate)
        {
            if (!weekdaysBySubject.TryGetValue(subjectId, out var weekdays) || weekdays.Count == 0)
                return null;

            var floor = qStartDt ?? beforeDate.AddDays(-120);
            var d = beforeDate.Date.AddDays(-1);
            while (d >= floor)
            {
                if (weekdays.Contains((int)d.DayOfWeek))
                    return d;
                d = d.AddDays(-1);
            }
            return null;
        }

        DateTime? NextLessonDate(int subjectId, DateTime afterDate)
        {
            if (!weekdaysBySubject.TryGetValue(subjectId, out var weekdays) || weekdays.Count == 0)
                return null;

            var ceiling = qEndDt ?? afterDate.AddDays(120);
            var d = afterDate.Date.AddDays(1);
            while (d <= ceiling)
            {
                if (weekdays.Contains((int)d.DayOfWeek))
                    return d;
                d = d.AddDays(1);
            }
            return null;
        }

        var homeworks = await _context.Homeworks
            .Where(h => h.ClassId == classId && teacherSubjectIds.Contains(h.SubjectId)
                     && ((h.DateIssued >= baseDate && h.DateIssued < toExclusive) ||
                         (h.DateDue >= baseDate && h.DateDue < toExclusive)))
            .ToListAsync();

        var days = new List<object>();
        for (int i = 0; i < 6; i++)
        {
            var date = baseDate.AddDays(i);
            var dow = i + 1;

            bool inQuarter = true;
            if (qStartDt.HasValue && date < qStartDt.Value) inQuarter = false;
            if (qEndDt.HasValue && date > qEndDt.Value) inQuarter = false;

            var lessons = new List<object>();
            if (inQuarter)
            {
                var daySchedules = schedules.Where(sc => sc.DayOfWeek == dow)
                    .OrderBy(sc => sc.LessonNumber).ToList();

                foreach (var sc in daySchedules)
                {
                    var subjectId = sc.SubjectId ?? 0;

                    var hw = homeworks.FirstOrDefault(h => h.SubjectId == subjectId
                        && (h.DateIssued.Date == date || h.DateDue.Date == date));

                    var prevLesson = PrevLessonDate(subjectId, date);
                    var nextLesson = NextLessonDate(subjectId, date);

                    lessons.Add(new
                    {
                        lessonNumber = sc.LessonNumber,
                        subjectId = sc.SubjectId,
                        subjectName = sc.Subject?.Name,
                        teacherName = sc.Teacher?.User?.FullName,
                        cabinet = sc.Cabinet,
                        prevLessonDate = prevLesson,
                        nextLessonDate = nextLesson, 
                        homework = hw == null ? null : new
                        {
                            id = hw.Id,
                            description = hw.Description,
                            dateIssued = hw.DateIssued,
                            dateDue = hw.DateDue
                        }
                    });
                }
            }

            days.Add(new { date, dayOfWeek = dow, dayName = dayNames[dow], lessons });
        }

        return Ok(new
        {
            startDate = baseDate,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            quarterStart = quarter?.DateStart,
            quarterEnd = quarter?.DateEnd,
            days
        });
    }

    [HttpGet("journal")]
    public async Task<IActionResult> GetJournal([FromQuery] int classId,
                                                [FromQuery] int subjectId,
                                                [FromQuery] int? quarterId,
                                                [FromQuery] DateTime? from,
                                                [FromQuery] DateTime? to)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();

        var linked = await _context.TeacherSubjects.AnyAsync(ts =>
            ts.TeacherId == teacher.Id && ts.ClassId == classId && ts.SubjectId == subjectId);
        if (!linked) return Forbid();

        return Ok(await BuildJournalAsync(classId, subjectId, quarterId, from, to));
    }

    internal async Task<object> BuildJournalAsync(int classId, int subjectId,
                                                  int? quarterId,
                                                  DateTime? from, DateTime? to)
    {
        Quarter? quarter = null;
        if (quarterId.HasValue)
        {
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
            if (quarter != null)
            {
                from = ToDateTime(quarter.DateStart);
                to = ToEndOfDay(quarter.DateEnd);
            }
        }
        else
        {
            if (!from.HasValue && !to.HasValue)
            {
                quarter = await GetCurrentQuarterAsync();
                if (quarter != null)
                {
                    from = ToDateTime(quarter.DateStart);
                    to = ToEndOfDay(quarter.DateEnd);
                }
            }
        }

        var fromDate = (from ?? DateTime.UtcNow.AddDays(-30)).Date;
        var toDate = (to ?? DateTime.UtcNow).Date;
        if (fromDate > toDate) (fromDate, toDate) = (toDate, fromDate);

        var students = await _context.Students
            .Include(s => s.User)
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.User!.FullName)
            .Select(s => new { s.Id, Name = s.User!.FullName })
            .ToListAsync();

        var weekdaysQuery = _context.Schedules
            .Where(sc => sc.ClassId == classId && sc.SubjectId == subjectId);
        if (quarter != null)
            weekdaysQuery = weekdaysQuery.Where(sc => sc.QuarterId == quarter.Id);

        var weekdays = await weekdaysQuery
            .Select(sc => sc.DayOfWeek)
            .Distinct()
            .ToListAsync();

        var dates = new List<DateTime>();
        for (var d = fromDate; d <= toDate; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Sunday) continue;
            if (weekdays.Contains((int)d.DayOfWeek)) dates.Add(d);
        }

        var toExclusive = toDate.AddDays(1);
        var grades = await _context.Grades
            .Where(g => g.SubjectId == subjectId && g.Student!.ClassId == classId
                        && g.Date >= fromDate && g.Date < toExclusive).ToListAsync();
        var atts = await _context.Attendances
            .Where(a => a.SubjectId == subjectId && a.Student!.ClassId == classId
                        && a.Date >= fromDate && a.Date < toExclusive).ToListAsync();

        var homeworks = await _context.Homeworks
            .Where(h => h.ClassId == classId && h.SubjectId == subjectId
                     && h.DateDue >= fromDate && h.DateDue < toExclusive)
            .ToListAsync();

        var hwIdList = homeworks.Select(h => h.Id).ToList();
        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hwIdList.Contains(hs.HomeworkId ?? 0))
            .ToListAsync();

        var rows = students.Select(st => new
        {
            studentId = st.Id,
            studentName = st.Name,
            cells = dates.Select(dt =>
            {
                var g = grades.FirstOrDefault(x => x.StudentId == st.Id && x.Date.Date == dt);
                var a = atts.FirstOrDefault(x => x.StudentId == st.Id && x.Date.Date == dt);
                var hw = homeworks.FirstOrDefault(x => x.DateDue.Date == dt);
                HomeworkStatus? hwSt = null;
                if (hw != null)
                    hwSt = hwStatuses.FirstOrDefault(x => x.HomeworkId == hw.Id && x.StudentId == st.Id);

                return new
                {
                    date = dt,
                    gradeId = g?.Id,
                    grade = g == null ? (int?)null : g.Value,
                    gradeType = g?.Type,
                    gradeComment = g?.Comment,
                    attendanceId = a?.Id,
                    attendance = a?.Status,
                    attendanceComment = a?.Comment,
                    homeworkId = hw == null ? (int?)null : hw.Id,
                    homeworkDescription = hw?.Description,
                    homeworkDateIssued = hw?.DateIssued,
                    homeworkDateDue = hw?.DateDue,
                    homeworkStatus = hwSt?.Status,
                    homeworkDateSubmitted = hwSt?.DateSubmitted
                };
            }).ToList()
        }).ToList();

        return new
        {
            classId,
            subjectId,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            quarterYear = quarter?.AcademicYear,
            quarterStart = quarter?.DateStart,
            quarterEnd = quarter?.DateEnd,
            from = fromDate,
            to = toDate,
            dates,
            students,
            rows
        };
    }

    [HttpGet("grades")]
    public async Task<IActionResult> GetGrades([FromQuery] int? classId, [FromQuery] int? subjectId, [FromQuery] int? studentId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var q = _context.Grades.Include(g => g.Student).ThenInclude(s => s!.User)
            .Include(g => g.Subject).Where(g => g.TeacherId == teacher.Id);
        if (subjectId.HasValue) q = q.Where(g => g.SubjectId == subjectId);
        if (studentId.HasValue) q = q.Where(g => g.StudentId == studentId);
        if (classId.HasValue) q = q.Where(g => g.Student!.ClassId == classId);
        return Ok(await q.ToListAsync());
    }

    [HttpPost("grades")]
    public async Task<IActionResult> CreateGrade([FromBody] Grade grade)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        grade.TeacherId = teacher.Id;
        if (grade.Date == default) grade.Date = DateTime.UtcNow;
        _context.Grades.Add(grade);
        await _context.SaveChangesAsync();
        return Ok(grade);
    }

    [HttpPost("grades/bulk")]
    public async Task<IActionResult> SaveGradesBulk([FromBody] List<Grade> grades)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        if (grades == null || grades.Count == 0) return BadRequest(new { message = "Список оценок пуст" });
        foreach (var g in grades)
        {
            g.TeacherId = teacher.Id;
            if (g.Date == default) g.Date = DateTime.UtcNow;
            if (g.Id > 0)
            {
                var ex = await _context.Grades.FirstOrDefaultAsync(x => x.Id == g.Id && x.TeacherId == teacher.Id);
                if (ex == null) continue;
                ex.Value = g.Value; ex.Type = g.Type; ex.Comment = g.Comment; ex.Date = g.Date;
            }
            else _context.Grades.Add(g);
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = "Оценки сохранены" });
    }

    [HttpPut("grades/{id:int}")]
    public async Task<IActionResult> UpdateGrade(int id, [FromBody] Grade update)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var grade = await _context.Grades.FirstOrDefaultAsync(g => g.Id == id && g.TeacherId == teacher.Id);
        if (grade == null) return NotFound();
        grade.Value = update.Value; grade.Type = update.Type; grade.Comment = update.Comment;
        if (update.Date != default) grade.Date = update.Date;
        await _context.SaveChangesAsync();
        return Ok(grade);
    }

    [HttpDelete("grades/{id:int}")]
    public async Task<IActionResult> DeleteGrade(int id)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var grade = await _context.Grades.FirstOrDefaultAsync(g => g.Id == id && g.TeacherId == teacher.Id);
        if (grade == null) return NotFound();
        _context.Grades.Remove(grade);
        await _context.SaveChangesAsync();
        return Ok();
    }


    [HttpGet("attendance")]
    public async Task<IActionResult> GetAttendance([FromQuery] int? classId, [FromQuery] int? subjectId, [FromQuery] DateTime? date)
    {
        var q = _context.Attendances.Include(a => a.Student).ThenInclude(s => s!.User)
            .Include(a => a.Subject).AsQueryable();
        if (subjectId.HasValue) q = q.Where(a => a.SubjectId == subjectId);
        if (classId.HasValue) q = q.Where(a => a.Student!.ClassId == classId);
        if (date.HasValue) q = q.Where(a => a.Date.Date == date.Value.Date);
        return Ok(await q.ToListAsync());
    }

    [HttpPost("attendance")]
    public async Task<IActionResult> MarkAttendance([FromBody] Attendance att)
    {
        if (att.Date == default) att.Date = DateTime.UtcNow;
        _context.Attendances.Add(att);
        await _context.SaveChangesAsync();
        return Ok(att);
    }

    [HttpPost("attendance/bulk")]
    public async Task<IActionResult> MarkAttendanceBulk([FromBody] List<Attendance> list)
    {
        if (list == null || list.Count == 0) return BadRequest(new { message = "Список пуст" });
        foreach (var a in list)
        {
            if (a.Date == default) a.Date = DateTime.UtcNow;
            _context.Attendances.Add(a);
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = "Посещаемость сохранена" });
    }

    [HttpPut("attendance/{id:int}")]
    public async Task<IActionResult> UpdateAttendance(int id, [FromBody] Attendance update)
    {
        var a = await _context.Attendances.FindAsync(id);
        if (a == null) return NotFound();
        a.Status = update.Status; a.Comment = update.Comment;
        await _context.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("attendance/{id:int}")]
    public async Task<IActionResult> DeleteAttendance(int id)
    {
        var a = await _context.Attendances.FindAsync(id);
        if (a == null) return NotFound();
        _context.Attendances.Remove(a);
        await _context.SaveChangesAsync();
        return Ok();
    }


    [HttpGet("homework")]
    public async Task<IActionResult> GetHomework([FromQuery] int? classId, [FromQuery] int? subjectId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var q = _context.Homeworks.Include(h => h.Class).Include(h => h.Subject)
            .Where(h => h.TeacherId == teacher.Id);
        if (classId.HasValue) q = q.Where(h => h.ClassId == classId);
        if (subjectId.HasValue) q = q.Where(h => h.SubjectId == subjectId);
        return Ok(await q.ToListAsync());
    }

    [HttpPost("homework")]
    public async Task<IActionResult> CreateHomework([FromBody] Homework hw)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        hw.TeacherId = teacher.Id;
        _context.Homeworks.Add(hw);
        await _context.SaveChangesAsync();
        return Ok(hw);
    }

    [HttpPut("homework/{id:int}")]
    public async Task<IActionResult> UpdateHomework(int id, [FromBody] Homework update)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var hw = await _context.Homeworks.FirstOrDefaultAsync(h => h.Id == id && h.TeacherId == teacher.Id);
        if (hw == null) return NotFound();
        hw.SubjectId = update.SubjectId; hw.ClassId = update.ClassId;
        hw.DateIssued = update.DateIssued; hw.DateDue = update.DateDue;
        hw.Description = update.Description;
        await _context.SaveChangesAsync();
        return Ok(hw);
    }

    [HttpDelete("homework/{id:int}")]
    public async Task<IActionResult> DeleteHomework(int id)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var hw = await _context.Homeworks.FirstOrDefaultAsync(h => h.Id == id && h.TeacherId == teacher.Id);
        if (hw == null) return NotFound();
        _context.Homeworks.Remove(hw);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("homework/{homeworkId:int}/statuses")]
    public async Task<IActionResult> GetHomeworkStatuses(int homeworkId)
    {
        var statuses = await _context.HomeworkStatuses
            .Where(hs => hs.HomeworkId == homeworkId)
            .Include(hs => hs.Student).ThenInclude(s => s!.User)
            .ToListAsync();
        return Ok(statuses);
    }

    [HttpPost("homework-statuses")]
    public async Task<IActionResult> UpsertHomeworkStatus([FromBody] HomeworkStatus hs)
    {
        var existing = await _context.HomeworkStatuses
            .FirstOrDefaultAsync(x => x.HomeworkId == hs.HomeworkId && x.StudentId == hs.StudentId);
        if (existing != null)
        {
            existing.Status = hs.Status;
            existing.DateSubmitted = hs.DateSubmitted;
        }
        else _context.HomeworkStatuses.Add(hs);
        await _context.SaveChangesAsync();
        return Ok();
    }


    [HttpGet("analytics/{classId:int}/{subjectId:int}")]
    public async Task<IActionResult> GetClassAnalytics(int classId, int subjectId, [FromQuery] int? quarterId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();
        var linked = await _context.TeacherSubjects.AnyAsync(ts =>
            ts.TeacherId == teacher.Id && ts.ClassId == classId && ts.SubjectId == subjectId);
        if (!linked) return Forbid();

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

        var gradesQ = _context.Grades
            .Where(g => g.SubjectId == subjectId && g.Student!.ClassId == classId);
        var attQ = _context.Attendances
            .Where(a => a.SubjectId == subjectId && a.Student!.ClassId == classId);

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

        var students = await _context.Students
            .Include(s => s.User)
            .Where(s => s.ClassId == classId)
            .ToListAsync();

        var hwQuery = _context.Homeworks
            .Where(h => h.ClassId == classId && h.SubjectId == subjectId);
        if (from.HasValue) hwQuery = hwQuery.Where(h => h.DateIssued >= from);
        if (to.HasValue) hwQuery = hwQuery.Where(h => h.DateIssued <= to);

        var hwIds = await hwQuery.Select(h => h.Id).ToListAsync();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hwIds.Contains(hs.HomeworkId ?? 0))
            .ToListAsync();

        var expectedSubmissions = hwIds.Count * students.Count;
        var doneSubmissions = hwStatuses.Count(hs =>
        {
            var s = (hs.Status ?? "").Trim().ToLower();
            return s == "выполнено" || s == "сдано" || s == "готово" || s == "done";
        });
        var homeworkCompletion = expectedSubmissions > 0
            ? Math.Round((double)doneSubmissions / expectedSubmissions * 100, 0)
            : 0;

        var byStudent = grades.GroupBy(g => g.StudentId)
            .Select(g => new
            {
                StudentId = g.Key,
                Average = Math.Round(g.Average(x => x.Value), 2),
                Count = g.Count(),
                Name = students.FirstOrDefault(s => s.Id == g.Key)?.User?.FullName ?? $"ID {g.Key}"
            })
            .OrderByDescending(x => x.Average)
            .ThenByDescending(x => x.Count)
            .ToList();

        var topStudents = byStudent.Take(5).ToList();

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
            var s = (a.Status ?? "").Trim().ToLower();
            if (s == "н" || s == "отсутствовал" || s == "отсутствовала") absent++;
            else if (s == "уважительная") valid++;
            else present++;
        }
        var attendanceDistribution = new[]
        {
            new { label = "Присутствовал", count = present, color = "#4a90e2" },
            new { label = "Отсутствовал",  count = absent,  color = "#d0021b" },
            new { label = "Уважительная",  count = valid,   color = "#f5a623" }
        };

        var monthsRu = new[] { "Янв", "Фев", "Мар", "Апр", "Май", "Июн",
                               "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
        var now = DateTime.UtcNow;
        var monthlyDynamics = new List<object>();
        for (int i = 5; i >= 0; i--)
        {
            var m = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
            var next = m.AddMonths(1);
            var mGrades = grades.Where(g => g.Date >= m && g.Date < next).ToList();
            monthlyDynamics.Add(new
            {
                label = monthsRu[m.Month - 1],
                year = m.Year,
                average = mGrades.Any() ? Math.Round(mGrades.Average(g => g.Value), 2) : 0,
                count = mGrades.Count
            });
        }

        return Ok(new
        {
            quarterId,
            averageGrade = grades.Any() ? Math.Round(grades.Average(g => g.Value), 2) : 0,
            totalGrades = grades.Count,
            totalStudents = students.Count,
            attendanceRate = attendance.Any()
                ? Math.Round((double)(attendance.Count - absent) / attendance.Count * 100, 1)
                : 100.0,
            homeworkCompletion,
            gradeDistribution,
            attendanceDistribution,
            monthlyDynamics,
            topStudents
        });
    }

    [HttpGet("analytics/{classId:int}/{subjectId:int}/export")]
    public async Task<IActionResult> ExportAnalytics(int classId, int subjectId, [FromQuery] int? quarterId)
    {
        var teacher = await GetCurrentTeacherAsync();
        if (teacher == null) return NotFound();

        var linked = await _context.TeacherSubjects.AnyAsync(ts =>
            ts.TeacherId == teacher.Id && ts.ClassId == classId && ts.SubjectId == subjectId);
        if (!linked) return Forbid();

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

        var students = await _context.Students
            .Include(s => s.User)
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.User!.FullName)
            .ToListAsync();

        var sb = new StringBuilder();

        sb.Append('\uFEFF');
        sb.Append("Ученик;Средний балл;Кол-во оценок;Пропусков\r\n");

        foreach (var s in students)
        {
            var gradesQ = _context.Grades
                .Where(g => g.StudentId == s.Id && g.SubjectId == subjectId);

            var attQ = _context.Attendances
                .Where(a => a.StudentId == s.Id && a.SubjectId == subjectId
                         && (a.Status == "н"
                          || a.Status == "отсутствовал"
                          || a.Status == "отсутствовала"));

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
            var absences = await attQ.CountAsync();

            var avg = grades.Count > 0 ? grades.Average(g => g.Value) : 0;
            var avgStr = avg.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            var name = s.User?.FullName ?? $"Ученик #{s.Id}";

            sb.Append(name).Append(';')
              .Append(avgStr).Append(';')
              .Append(grades.Count).Append(';')
              .Append(absences).Append("\r\n");
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = quarterId.HasValue
            ? $"analytics_class{classId}_subj{subjectId}_q{quarterId}.csv"
            : $"analytics_class{classId}_subj{subjectId}.csv";

        return File(bytes, "text/csv; charset=utf-8", fileName);
    }
}