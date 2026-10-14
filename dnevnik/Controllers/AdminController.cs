using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dnevnik.Data;
using dnevnik.Entities;
using dnevnik.Services;
using System.Text;

namespace dnevnik.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Администратор,Admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminController> _logger;

    public AdminController(ApplicationDbContext context,
                           IPasswordHasher passwordHasher,
                           ILogger<AdminController> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    private int CurrentUserId()
    {
        var raw = User.FindFirst("userId")?.Value;
        return int.TryParse(raw, out var id) ? id : 0;
    }

    private const int ROLE_ADMIN = 1;
    private const int ROLE_TEACHER = 2;
    private const int ROLE_STUDENT = 3;
    private const int ROLE_PARENT = 4;

    // ================= USERS =================

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .AsNoTracking()
            .Select(u => new
            {
                u.Id,
                u.Login,
                u.FullName,
                u.Email,
                u.IsActive,
                u.CreatedAt,
                RoleId = u.RoleId,
                Role = u.Role == null ? null : new { u.Role.Id, u.Role.Name }
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.Login,
                u.FullName,
                u.Email,
                u.IsActive,
                u.CreatedAt,
                RoleId = u.RoleId,
                Role = u.Role == null ? null : new { u.Role.Id, u.Role.Name }
            })
            .FirstOrDefaultAsync();

        if (user == null) return NotFound();
        return Ok(user);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] User req,
                                                [FromQuery] string? password = null,
                                                [FromQuery] string? phone = null,
                                                [FromQuery] int? classId = null,
                                                [FromQuery] DateTime? birthDate = null)
    {
        // req — это User, но пароль передаётся отдельно через query, т.к. DTO не используем.
        // Если удобнее — принимайте JSON с полями login/password/... и разбирайте вручную.
        if (req == null ||
            string.IsNullOrWhiteSpace(req.Login) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(req.FullName))
        {
            return BadRequest(new { message = "Логин, пароль и ФИО обязательны" });
        }

        if (await _context.Users.AnyAsync(u => u.Login == req.Login))
            return BadRequest(new { message = "Логин уже занят" });

        if (req.RoleId == ROLE_STUDENT && classId.HasValue)
        {
            if (!await _context.Classes.AnyAsync(c => c.Id == classId.Value))
                return BadRequest(new { message = "Указанный класс не найден" });
        }

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var user = new User
            {
                Login = req.Login,
                PasswordHash = _passwordHasher.Hash(password),
                FullName = req.FullName,
                Email = req.Email,
                RoleId = req.RoleId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            switch (req.RoleId)
            {
                case ROLE_TEACHER:
                    _context.Teachers.Add(new Teacher { UserId = user.Id, Phone = phone });
                    break;
                case ROLE_STUDENT:
                    _context.Students.Add(new Student
                    {
                        UserId = user.Id,
                        ClassId = classId,
                        BirthDate = birthDate
                    });
                    break;
                case ROLE_PARENT:
                    _context.Parents.Add(new Parent { UserId = user.Id, Phone = phone });
                    break;
            }

            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new
            {
                user.Id,
                user.Login,
                user.FullName,
                user.Email,
                user.RoleId,
                user.IsActive
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Ошибка создания пользователя Login={Login}", req.Login);
            return StatusCode(500, new { message = "Ошибка при создании пользователя: " + ex.Message });
        }
    }

    [HttpPut("users/{id:int}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] User req)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Login) && req.Login != user.Login)
        {
            if (await _context.Users.AnyAsync(u => u.Login == req.Login))
                return BadRequest(new { message = "Логин уже занят" });
            user.Login = req.Login;
        }

        if (!string.IsNullOrWhiteSpace(req.FullName)) user.FullName = req.FullName;
        if (req.Email != null) user.Email = req.Email;

        if (req.RoleId.HasValue && req.RoleId != user.RoleId)
        {
            var oldRole = user.RoleId;
            user.RoleId = req.RoleId;
            await _context.SaveChangesAsync();
            await EnsureRoleProfile(user);
        }
        else
        {
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            user.Id,
            user.Login,
            user.FullName,
            user.Email,
            user.RoleId,
            user.IsActive
        });
    }

    private async Task EnsureRoleProfile(User user)
    {
        switch (user.RoleId)
        {
            case ROLE_TEACHER:
                if (!await _context.Teachers.AnyAsync(t => t.UserId == user.Id))
                    _context.Teachers.Add(new Teacher { UserId = user.Id });
                break;
            case ROLE_STUDENT:
                if (!await _context.Students.AnyAsync(s => s.UserId == user.Id))
                    _context.Students.Add(new Student { UserId = user.Id });
                break;
            case ROLE_PARENT:
                if (!await _context.Parents.AnyAsync(p => p.UserId == user.Id))
                    _context.Parents.Add(new Parent { UserId = user.Id });
                break;
        }
        await _context.SaveChangesAsync();
    }

    [HttpPost("users/{id:int}/block")]
    public async Task<IActionResult> BlockUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.IsActive = false;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Пользователь заблокирован" });
    }

    [HttpPost("users/{id:int}/unblock")]
    public async Task<IActionResult> UnblockUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        user.IsActive = true;
        await _context.SaveChangesAsync();
        return Ok(new { message = "Пользователь разблокирован" });
    }

    [HttpPost("users/{id:int}/role/{roleId:int}")]
    public async Task<IActionResult> AssignRole(int id, int roleId)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (!await _context.Roles.AnyAsync(r => r.Id == roleId))
            return BadRequest(new { message = "Роль не найдена" });

        user.RoleId = roleId;
        await _context.SaveChangesAsync();
        await EnsureRoleProfile(user);

        return Ok(new { user.Id, user.Login, user.RoleId });
    }

    [HttpPost("users/{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, [FromBody] string newPassword)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();
        if (string.IsNullOrWhiteSpace(newPassword))
            return BadRequest(new { message = "Пароль не может быть пустым" });

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Пароль обновлён" });
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var student = await _context.Students.FirstOrDefaultAsync(s => s.UserId == id);
            if (student != null)
            {
                var parentLinks = await _context.ParentStudents
                    .Where(ps => ps.StudentId == student.Id).ToListAsync();
                _context.ParentStudents.RemoveRange(parentLinks);

                var grades = await _context.Grades
                    .Where(g => g.StudentId == student.Id).ToListAsync();
                _context.Grades.RemoveRange(grades);

                var atts = await _context.Attendances
                    .Where(a => a.StudentId == student.Id).ToListAsync();
                _context.Attendances.RemoveRange(atts);

                var hwStatuses = await _context.HomeworkStatuses
                    .Where(h => h.StudentId == student.Id).ToListAsync();
                _context.HomeworkStatuses.RemoveRange(hwStatuses);

                _context.Students.Remove(student);
            }

            var teacher = await _context.Teachers.FirstOrDefaultAsync(t => t.UserId == id);
            if (teacher != null)
            {
                bool hasSchedule = await _context.Schedules.AnyAsync(s => s.TeacherId == teacher.Id);
                bool hasGrades = await _context.Grades.AnyAsync(g => g.TeacherId == teacher.Id);
                bool hasHw = await _context.Homeworks.AnyAsync(h => h.TeacherId == teacher.Id);
                bool hasTs = await _context.TeacherSubjects.AnyAsync(ts => ts.TeacherId == teacher.Id);

                if (hasSchedule || hasGrades || hasHw || hasTs)
                {
                    await tx.RollbackAsync();
                    return BadRequest(new
                    {
                        message = "Нельзя удалить учителя: есть связанные расписания, оценки, задания или назначения предметов."
                    });
                }
                _context.Teachers.Remove(teacher);
            }

            var parent = await _context.Parents.FirstOrDefaultAsync(p => p.UserId == id);
            if (parent != null)
            {
                var links = await _context.ParentStudents
                    .Where(ps => ps.ParentId == parent.Id).ToListAsync();
                _context.ParentStudents.RemoveRange(links);
                _context.Parents.Remove(parent);
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            await tx.CommitAsync();
            return Ok();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Ошибка удаления пользователя Id={UserId}", id);
            return StatusCode(500, new { message = "Ошибка удаления: " + ex.Message });
        }
    }

    // ================= ROLES =================

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
        => Ok(await _context.Roles.AsNoTracking()
            .Select(r => new { r.Id, r.Name }).ToListAsync());

    // ================= CLASSES =================

    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses()
        => Ok(await _context.Classes.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.Year }).ToListAsync());

    [HttpPost("classes")]
    public async Task<IActionResult> CreateClass([FromBody] SchoolClass cls)
    {
        _context.Classes.Add(cls);
        await _context.SaveChangesAsync();
        return Ok(new { cls.Id, cls.Name, cls.Year });
    }

    [HttpPut("classes/{id:int}")]
    public async Task<IActionResult> UpdateClass(int id, [FromBody] SchoolClass update)
    {
        var cls = await _context.Classes.FindAsync(id);
        if (cls == null) return NotFound();
        cls.Name = update.Name;
        cls.Year = update.Year;
        await _context.SaveChangesAsync();
        return Ok(new { cls.Id, cls.Name, cls.Year });
    }

    [HttpDelete("classes/{id:int}")]
    public async Task<IActionResult> DeleteClass(int id)
    {
        var cls = await _context.Classes.FindAsync(id);
        if (cls == null) return NotFound();
        _context.Classes.Remove(cls);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= SUBJECTS =================

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
        => Ok(await _context.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Description }).ToListAsync());

    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject([FromBody] Subject s)
    {
        _context.Subjects.Add(s);
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.Name, s.Description });
    }

    [HttpPut("subjects/{id:int}")]
    public async Task<IActionResult> UpdateSubject(int id, [FromBody] Subject update)
    {
        var s = await _context.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        s.Name = update.Name;
        s.Description = update.Description;
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.Name, s.Description });
    }

    [HttpDelete("subjects/{id:int}")]
    public async Task<IActionResult> DeleteSubject(int id)
    {
        var s = await _context.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        _context.Subjects.Remove(s);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= TEACHERS =================

    [HttpGet("teachers")]
    public async Task<IActionResult> GetTeachers()
        => Ok(await _context.Teachers.AsNoTracking()
            .Select(t => new
            {
                t.Id,
                t.Phone,
                t.UserId,
                User = t.User == null ? null : new
                {
                    t.User.Id,
                    t.User.Login,
                    t.User.FullName,
                    t.User.Email
                }
            }).ToListAsync());

    [HttpPost("teachers")]
    public async Task<IActionResult> CreateTeacher([FromBody] Teacher t)
    {
        _context.Teachers.Add(t);
        await _context.SaveChangesAsync();
        return Ok(new { t.Id, t.UserId, t.Phone });
    }

    [HttpPut("teachers/{id:int}")]
    public async Task<IActionResult> UpdateTeacher(int id, [FromBody] Teacher update)
    {
        var t = await _context.Teachers.FindAsync(id);
        if (t == null) return NotFound();
        t.Phone = update.Phone;
        t.UserId = update.UserId;
        await _context.SaveChangesAsync();
        return Ok(new { t.Id, t.UserId, t.Phone });
    }

    [HttpDelete("teachers/{id:int}")]
    public async Task<IActionResult> DeleteTeacher(int id)
    {
        var t = await _context.Teachers.FindAsync(id);
        if (t == null) return NotFound();
        _context.Teachers.Remove(t);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= TEACHER-SUBJECTS =================

    [HttpPost("teacher-subjects")]
    public async Task<IActionResult> CreateTeacherSubject([FromBody] TeacherSubject ts)
    {
        _context.TeacherSubjects.Add(ts);
        await _context.SaveChangesAsync();
        return Ok(new { ts.Id, ts.TeacherId, ts.SubjectId, ts.ClassId });
    }

    [HttpDelete("teacher-subjects/{id:int}")]
    public async Task<IActionResult> DeleteTeacherSubject(int id)
    {
        var ts = await _context.TeacherSubjects.FindAsync(id);
        if (ts == null) return NotFound();
        _context.TeacherSubjects.Remove(ts);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= STUDENTS =================

    [HttpGet("students")]
    public async Task<IActionResult> GetStudents()
        => Ok(await _context.Students.AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.ClassId,
                s.BirthDate,
                User = s.User == null ? null : new
                {
                    s.User.Id,
                    s.User.Login,
                    s.User.FullName,
                    s.User.Email
                },
                Class = s.Class == null ? null : new { s.Class.Id, s.Class.Name, s.Class.Year }
            }).ToListAsync());

    [HttpPost("students")]
    public async Task<IActionResult> CreateStudent([FromBody] Student s)
    {
        _context.Students.Add(s);
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.UserId, s.ClassId, s.BirthDate });
    }

    [HttpPut("students/{id:int}")]
    public async Task<IActionResult> UpdateStudent(int id, [FromBody] Student update)
    {
        var s = await _context.Students.FindAsync(id);
        if (s == null) return NotFound();
        s.UserId = update.UserId;
        s.ClassId = update.ClassId;
        s.BirthDate = update.BirthDate;
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.UserId, s.ClassId, s.BirthDate });
    }

    [HttpDelete("students/{id:int}")]
    public async Task<IActionResult> DeleteStudent(int id)
    {
        var s = await _context.Students.FindAsync(id);
        if (s == null) return NotFound();
        _context.Students.Remove(s);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= PARENTS =================

    [HttpGet("parents")]
    public async Task<IActionResult> GetParents()
        => Ok(await _context.Parents.AsNoTracking()
            .Select(p => new
            {
                p.Id,
                p.UserId,
                p.Phone,
                User = p.User == null ? null : new
                {
                    p.User.Id,
                    p.User.Login,
                    p.User.FullName,
                    p.User.Email
                }
            }).ToListAsync());

    [HttpPost("parents")]
    public async Task<IActionResult> CreateParent([FromBody] Parent p)
    {
        _context.Parents.Add(p);
        await _context.SaveChangesAsync();
        return Ok(new { p.Id, p.UserId, p.Phone });
    }

    [HttpPut("parents/{id:int}")]
    public async Task<IActionResult> UpdateParent(int id, [FromBody] Parent update)
    {
        var p = await _context.Parents.FindAsync(id);
        if (p == null) return NotFound();
        p.UserId = update.UserId;
        p.Phone = update.Phone;
        await _context.SaveChangesAsync();
        return Ok(new { p.Id, p.UserId, p.Phone });
    }

    [HttpDelete("parents/{id:int}")]
    public async Task<IActionResult> DeleteParent(int id)
    {
        var p = await _context.Parents.FindAsync(id);
        if (p == null) return NotFound();
        _context.Parents.Remove(p);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("parents/{parentId:int}/students")]
    public async Task<IActionResult> GetParentStudents(int parentId)
        => Ok(await _context.ParentStudents.AsNoTracking()
            .Where(ps => ps.ParentId == parentId)
            .Select(ps => new
            {
                ps.Id,
                ps.ParentId,
                ps.StudentId,
                Student = ps.Student == null ? null : new
                {
                    ps.Student.Id,
                    ps.Student.UserId,
                    ps.Student.ClassId,
                    User = ps.Student.User == null ? null : new
                    {
                        ps.Student.User.Id,
                        ps.Student.User.FullName,
                        ps.Student.User.Login
                    }
                }
            }).ToListAsync());

    [HttpPost("parents/{parentId:int}/students/{studentId:int}")]
    public async Task<IActionResult> LinkParentStudent(int parentId, int studentId)
    {
        if (await _context.ParentStudents.AnyAsync(ps => ps.ParentId == parentId && ps.StudentId == studentId))
            return BadRequest(new { message = "Связь уже существует" });

        _context.ParentStudents.Add(new ParentStudent { ParentId = parentId, StudentId = studentId });
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("parents/{parentId:int}/students/{studentId:int}")]
    public async Task<IActionResult> UnlinkParentStudent(int parentId, int studentId)
    {
        var link = await _context.ParentStudents
            .FirstOrDefaultAsync(ps => ps.ParentId == parentId && ps.StudentId == studentId);
        if (link == null) return NotFound();
        _context.ParentStudents.Remove(link);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= QUARTERS =================

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

    [HttpPost("quarters")]
    public async Task<IActionResult> CreateQuarter([FromBody] Quarter req)
    {
        if (req == null) return BadRequest(new { message = "Пустой запрос" });
        if (req.QuarterNumber < 1 || req.QuarterNumber > 4)
            return BadRequest(new { message = "Номер четверти должен быть 1..4" });
        if (req.DateStart >= req.DateEnd)
            return BadRequest(new { message = "Дата начала должна быть раньше окончания" });

        if (await _context.Quarters.AnyAsync(q =>
                q.AcademicYear == req.AcademicYear && q.QuarterNumber == req.QuarterNumber))
            return BadRequest(new { message = "Такая четверть уже существует" });

        var q = new Quarter
        {
            AcademicYear = req.AcademicYear,
            QuarterNumber = req.QuarterNumber,
            DateStart = req.DateStart,
            DateEnd = req.DateEnd
        };
        _context.Quarters.Add(q);
        await _context.SaveChangesAsync();
        return Ok(new { q.Id });
    }

    [HttpPut("quarters/{id:int}")]
    public async Task<IActionResult> UpdateQuarter(int id, [FromBody] Quarter req)
    {
        var q = await _context.Quarters.FindAsync(id);
        if (q == null) return NotFound();

        if (req.AcademicYear > 0) q.AcademicYear = req.AcademicYear;
        if (req.QuarterNumber > 0) q.QuarterNumber = req.QuarterNumber;
        if (req.DateStart != default) q.DateStart = req.DateStart;
        if (req.DateEnd != default) q.DateEnd = req.DateEnd;

        if (q.DateStart >= q.DateEnd)
            return BadRequest(new { message = "Дата начала должна быть раньше окончания" });

        await _context.SaveChangesAsync();
        return Ok(new { q.Id });
    }

    [HttpDelete("quarters/{id:int}")]
    public async Task<IActionResult> DeleteQuarter(int id)
    {
        var q = await _context.Quarters.FindAsync(id);
        if (q == null) return NotFound();
        _context.Quarters.Remove(q);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= SCHEDULE =================

    [HttpGet("schedule")]
    public async Task<IActionResult> GetSchedule([FromQuery] int? classId, [FromQuery] int? quarterId)
    {
        var q = _context.Schedules.AsNoTracking().AsQueryable();
        if (classId.HasValue) q = q.Where(s => s.ClassId == classId);
        if (quarterId.HasValue) q = q.Where(s => s.QuarterId == quarterId.Value);

        var items = await q
            .OrderBy(s => s.QuarterId).ThenBy(s => s.DayOfWeek).ThenBy(s => s.LessonNumber)
            .Select(s => new
            {
                s.Id,
                s.QuarterId,
                s.ClassId,
                s.SubjectId,
                s.TeacherId,
                s.DayOfWeek,
                s.LessonNumber,
                s.Cabinet,
                Class = s.Class == null ? null : new { s.Class.Id, s.Class.Name },
                Subject = s.Subject == null ? null : new { s.Subject.Id, s.Subject.Name },
                Quarter = s.Quarter == null ? null : new
                {
                    s.Quarter.Id,
                    s.Quarter.AcademicYear,
                    s.Quarter.QuarterNumber,
                    s.Quarter.DateStart,
                    s.Quarter.DateEnd
                },
                Teacher = s.Teacher == null ? null : new
                {
                    s.Teacher.Id,
                    s.Teacher.Phone,
                    User = s.Teacher.User == null ? null : new
                    {
                        s.Teacher.User.Id,
                        s.Teacher.User.FullName,
                        s.Teacher.User.Login
                    }
                }
            }).ToListAsync();

        return Ok(items);
    }

    [HttpPost("schedule")]
    public async Task<IActionResult> CreateSchedule([FromBody] Schedule s)
    {
        if (s.QuarterId <= 0) return BadRequest(new { message = "Укажите четверть" });
        if (!await _context.Quarters.AnyAsync(q => q.Id == s.QuarterId))
            return BadRequest(new { message = "Четверть не найдена" });

        _context.Schedules.Add(s);
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Конфликт: у класса или учителя уже есть урок в этот слот" });
        }
        return Ok(new { s.Id });
    }

    [HttpPut("schedule/{id:int}")]
    public async Task<IActionResult> UpdateSchedule(int id, [FromBody] Schedule update)
    {
        var s = await _context.Schedules.FindAsync(id);
        if (s == null) return NotFound();

        if (update.QuarterId > 0) s.QuarterId = update.QuarterId;
        s.ClassId = update.ClassId;
        s.SubjectId = update.SubjectId;
        s.TeacherId = update.TeacherId;
        s.DayOfWeek = update.DayOfWeek;
        s.LessonNumber = update.LessonNumber;
        s.Cabinet = update.Cabinet;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return BadRequest(new { message = "Конфликт: у класса или учителя уже есть урок в этот слот" });
        }
        return Ok(new { s.Id });
    }

    [HttpDelete("schedule/{id:int}")]
    public async Task<IActionResult> DeleteSchedule(int id)
    {
        var s = await _context.Schedules.FindAsync(id);
        if (s == null) return NotFound();
        _context.Schedules.Remove(s);
        await _context.SaveChangesAsync();
        return Ok();
    }

    // ================= ANALYTICS =================

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics()
    {
        try
        {
            var totalUsers = await _context.Users.CountAsync();
            var totalStudents = await _context.Students.CountAsync();
            var totalTeachers = await _context.Teachers.CountAsync();
            var totalParents = await _context.Parents.CountAsync();
            var totalClasses = await _context.Classes.CountAsync();

            var classes = await _context.Classes.AsNoTracking()
                .Select(c => new
                {
                    c.Id,
                    ClassName = c.Name,
                    StudentsCount = c.Students.Count,
                    AverageGrade = c.Students
                        .SelectMany(s => s.Grades)
                        .Select(g => (double?)g.Value)
                        .Average() ?? 0,
                    TotalAttendances = c.Students.SelectMany(s => s.Attendances).Count(),
                    PresentAttendances = c.Students.SelectMany(s => s.Attendances)
                        .Count(a => a.Status != "н" && a.Status != "отсутствовал")
                })
                .ToListAsync();

            var avgByClass = classes.Select(c => new
            {
                c.Id,
                c.ClassName,
                c.StudentsCount,
                c.AverageGrade
            }).ToList();

            var attendanceByClass = classes.Select(c => new
            {
                c.Id,
                c.ClassName,
                Total = c.TotalAttendances,
                Present = c.PresentAttendances,
                Rate = c.TotalAttendances == 0
                    ? 100.0
                    : (double)c.PresentAttendances / c.TotalAttendances * 100
            }).ToList();

            return Ok(new
            {
                totalUsers,
                totalStudents,
                totalTeachers,
                totalParents,
                totalClasses,
                avgByClass,
                attendanceByClass
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при построении аналитики");
            return StatusCode(500, new { message = "Ошибка при построении аналитики" });
        }
    }

    [HttpGet("analytics/export")]
    public async Task<IActionResult> ExportAnalytics()
    {
        try
        {
            var classes = await _context.Classes.AsNoTracking()
                .Select(c => new
                {
                    c.Name,
                    StudentsCount = c.Students.Count,
                    AverageGrade = c.Students
                        .SelectMany(s => s.Grades)
                        .Select(g => (double?)g.Value)
                        .Average() ?? 0
                })
                .ToListAsync();

            var sb = new StringBuilder();
            sb.AppendLine("Класс;Учеников;Средний балл");
            foreach (var c in classes)
                sb.AppendLine($"{c.Name};{c.StudentsCount};{c.AverageGrade:F2}");

            return File(Encoding.UTF8.GetBytes(sb.ToString()),
                "text/csv", $"school_analytics_{DateTime.UtcNow:yyyyMMdd}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка экспорта аналитики");
            return StatusCode(500, new { message = "Ошибка экспорта" });
        }
    }

    // ================= ЖУРНАЛ =================

    [HttpGet("journal")]
    public async Task<IActionResult> GetJournal([FromQuery] int classId,
                                                [FromQuery] int subjectId,
                                                [FromQuery] int? quarterId,
                                                [FromQuery] DateTime? from,
                                                [FromQuery] DateTime? to)
    {
        Quarter? quarter = null;
        if (quarterId.HasValue)
        {
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
            if (quarter != null)
            {
                from = new DateTime(quarter.DateStart.Year, quarter.DateStart.Month, quarter.DateStart.Day);
                to = new DateTime(quarter.DateEnd.Year, quarter.DateEnd.Month, quarter.DateEnd.Day, 23, 59, 59);
            }
        }
        else if (!from.HasValue && !to.HasValue)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            quarter = await _context.Quarters
                .Where(q => q.DateStart <= today && q.DateEnd >= today)
                .OrderBy(q => q.QuarterNumber)
                .FirstOrDefaultAsync();
            if (quarter != null)
            {
                from = new DateTime(quarter.DateStart.Year, quarter.DateStart.Month, quarter.DateStart.Day);
                to = new DateTime(quarter.DateEnd.Year, quarter.DateEnd.Month, quarter.DateEnd.Day, 23, 59, 59);
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
            .Select(sc => sc.DayOfWeek).Distinct().ToListAsync();

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

        var rows = students.Select(st => new
        {
            studentId = st.Id,
            studentName = st.Name,
            cells = dates.Select(dt =>
            {
                var g = grades.FirstOrDefault(x => x.StudentId == st.Id && x.Date.Date == dt);
                var a = atts.FirstOrDefault(x => x.StudentId == st.Id && x.Date.Date == dt);
                return new
                {
                    date = dt,
                    grade = g == null ? (int?)null : g.Value,
                    gradeType = g?.Type,
                    gradeComment = g?.Comment,
                    attendance = a?.Status,
                    attendanceComment = a?.Comment
                };
            }).ToList()
        }).ToList();

        return Ok(new
        {
            classId,
            subjectId,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            from = fromDate,
            to = toDate,
            dates,
            students,
            rows
        });
    }

    // ================= ДНЕВНИК =================

    [HttpGet("students-by-class/{classId:int}")]
    public async Task<IActionResult> GetStudentsByClass(int classId)
    {
        var students = await _context.Students
            .Include(s => s.User)
            .Where(s => s.ClassId == classId)
            .OrderBy(s => s.User!.FullName)
            .Select(s => new { s.Id, Name = s.User!.FullName })
            .ToListAsync();
        return Ok(students);
    }

    [HttpGet("diary")]
    public async Task<IActionResult> GetDiary([FromQuery] int classId,
                                              [FromQuery] int? subjectId,
                                              [FromQuery] int? studentId,
                                              [FromQuery] DateTime? weekStart)
    {
        return Ok(await BuildDiaryAsync(classId, subjectId, studentId, weekStart));
    }

    internal async Task<object> BuildDiaryAsync(int classId, int? subjectId,
                                                int? studentId, DateTime? weekStart)
    {
        var baseDate = (weekStart ?? DateTime.UtcNow).Date;
        while (baseDate.DayOfWeek != DayOfWeek.Monday) baseDate = baseDate.AddDays(-1);
        var toExclusive = baseDate.AddDays(7);

        var dMonday = DateOnly.FromDateTime(baseDate);
        var quarter = await _context.Quarters
            .Where(q => q.DateStart <= dMonday && q.DateEnd >= dMonday)
            .OrderBy(q => q.QuarterNumber)
            .FirstOrDefaultAsync();

        var studentsQuery = _context.Students.Include(s => s.User).Where(s => s.ClassId == classId);
        if (studentId.HasValue) studentsQuery = studentsQuery.Where(s => s.Id == studentId);

        var students = await studentsQuery
            .OrderBy(s => s.User!.FullName)
            .Select(s => new { s.Id, Name = s.User!.FullName })
            .ToListAsync();

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда",
                             "Четверг", "Пятница", "Суббота" };

        var scheduleQuery = _context.Schedules
            .Include(sc => sc.Subject).Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Where(sc => sc.ClassId == classId);
        if (quarter != null)
            scheduleQuery = scheduleQuery.Where(sc => sc.QuarterId == quarter.Id);
        if (subjectId.HasValue)
            scheduleQuery = scheduleQuery.Where(sc => sc.SubjectId == subjectId);

        var schedules = await scheduleQuery.ToListAsync();

        var hwQuery = _context.Homeworks
            .Where(h => h.ClassId == classId
                     && h.DateIssued >= baseDate && h.DateIssued < toExclusive);
        if (subjectId.HasValue) hwQuery = hwQuery.Where(h => h.SubjectId == subjectId);
        var homeworks = await hwQuery.ToListAsync();
        var hwIds = homeworks.Select(h => h.Id).ToList();

        var hwStatuses = await _context.HomeworkStatuses
            .Where(hs => hwIds.Contains(hs.HomeworkId ?? 0)).ToListAsync();

        var gradesQuery = _context.Grades
            .Where(g => g.Student!.ClassId == classId
                     && g.Date >= baseDate && g.Date < toExclusive);
        if (subjectId.HasValue) gradesQuery = gradesQuery.Where(g => g.SubjectId == subjectId);
        var grades = await gradesQuery.ToListAsync();

        var attQuery = _context.Attendances
            .Where(a => a.Student!.ClassId == classId
                     && a.Date >= baseDate && a.Date < toExclusive);
        if (subjectId.HasValue) attQuery = attQuery.Where(a => a.SubjectId == subjectId);
        var attendances = await attQuery.ToListAsync();

        var diaries = new List<object>();
        foreach (var st in students)
        {
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
                        hwStatus = hwStatuses.FirstOrDefault(x => x.HomeworkId == hw.Id && x.StudentId == st.Id);

                    var att = attendances.FirstOrDefault(a =>
                        a.StudentId == st.Id && a.SubjectId == sc.SubjectId && a.Date.Date == date);

                    var lessonGrades = grades
                        .Where(g => g.StudentId == st.Id && g.SubjectId == sc.SubjectId && g.Date.Date == date)
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
}