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

    private static readonly string[] DayNamesShort = { "", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб" };

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
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (req == null ||
            string.IsNullOrWhiteSpace(req.Login) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(req.FullName))
        {
            return BadRequest(new { message = "Логин, пароль и ФИО обязательны" });
        }

        if (password.Length < 6)
            return BadRequest(new { message = "Пароль должен содержать минимум 6 символов" });

        if (await _context.Users.AnyAsync(u => u.Login == req.Login))
            return BadRequest(new { message = "Логин уже занят" });

        if (req.RoleId == ROLE_STUDENT && classId.HasValue)
        {
            if (!await _context.Classes.AnyAsync(c => c.Id == classId.Value))
                return BadRequest(new { message = "Указанный класс не найден" });
        }

        if (req.RoleId < ROLE_ADMIN || req.RoleId > ROLE_PARENT)
            return BadRequest(new { message = "Некорректная роль" });

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
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        if (req.RoleId.HasValue && req.RoleId != user.RoleId)
        {
            if (user.RoleId == ROLE_ADMIN && req.RoleId != ROLE_ADMIN)
            {
                var adminCount = await _context.Users.CountAsync(u => u.RoleId == ROLE_ADMIN && u.IsActive);
                if (adminCount <= 1)
                    return BadRequest(new { message = "Нельзя изменить роль последнего администратора" });
            }
        }

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
            if (req.RoleId < ROLE_ADMIN || req.RoleId > ROLE_PARENT)
                return BadRequest(new { message = "Некорректная роль" });

            var oldRoleId = user.RoleId;
            user.RoleId = req.RoleId;
            await _context.SaveChangesAsync();

            if (oldRoleId == ROLE_TEACHER)
                _context.Teachers.RemoveRange(_context.Teachers.Where(t => t.UserId == user.Id));
            else if (oldRoleId == ROLE_STUDENT)
                _context.Students.RemoveRange(_context.Students.Where(s => s.UserId == user.Id));
            else if (oldRoleId == ROLE_PARENT)
                _context.Parents.RemoveRange(_context.Parents.Where(p => p.UserId == user.Id));

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

        if (id == CurrentUserId())
            return BadRequest(new { message = "Нельзя заблокировать самого себя" });

        if (user.RoleId == ROLE_ADMIN)
        {
            var adminCount = await _context.Users.CountAsync(u => u.RoleId == ROLE_ADMIN && u.IsActive);
            if (adminCount <= 1)
                return BadRequest(new { message = "Нельзя заблокировать последнего активного администратора" });
        }

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

        if (user.RoleId == ROLE_ADMIN && roleId != ROLE_ADMIN)
        {
            var adminCount = await _context.Users.CountAsync(u => u.RoleId == ROLE_ADMIN && u.IsActive);
            if (adminCount <= 1)
                return BadRequest(new { message = "Нельзя изменить роль последнего администратора" });
        }

        var oldRoleId = user.RoleId;
        user.RoleId = roleId;
        await _context.SaveChangesAsync();

        if (oldRoleId == ROLE_TEACHER)
            _context.Teachers.RemoveRange(_context.Teachers.Where(t => t.UserId == user.Id));
        else if (oldRoleId == ROLE_STUDENT)
            _context.Students.RemoveRange(_context.Students.Where(s => s.UserId == user.Id));
        else if (oldRoleId == ROLE_PARENT)
            _context.Parents.RemoveRange(_context.Parents.Where(p => p.UserId == user.Id));

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
        if (newPassword.Length < 6)
            return BadRequest(new { message = "Пароль должен содержать минимум 6 символов" });

        user.PasswordHash = _passwordHasher.Hash(newPassword);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Пароль обновлён" });
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles()
        => Ok(await _context.Roles.AsNoTracking()
            .Select(r => new { r.Id, r.Name }).ToListAsync());

    [HttpGet("classes")]
    public async Task<IActionResult> GetClasses()
        => Ok(await _context.Classes.AsNoTracking()
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Year,
                StudentsCount = c.Students.Count
            }).ToListAsync());

    [HttpPost("classes")]
    public async Task<IActionResult> CreateClass([FromBody] SchoolClass cls)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(cls.Name))
            return BadRequest(new { message = "Название класса обязательно" });
        if (cls.Year < 2000 || cls.Year > 2100)
            return BadRequest(new { message = "Некорректный год" });
        if (await _context.Classes.AnyAsync(c => c.Name == cls.Name && c.Year == cls.Year))
            return BadRequest(new { message = "Класс с таким названием и годом уже существует" });

        _context.Classes.Add(cls);
        await _context.SaveChangesAsync();
        return Ok(new { cls.Id, cls.Name, cls.Year });
    }

    [HttpPut("classes/{id:int}")]
    public async Task<IActionResult> UpdateClass(int id, [FromBody] SchoolClass update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var cls = await _context.Classes.FindAsync(id);
        if (cls == null) return NotFound();

        if (string.IsNullOrWhiteSpace(update.Name))
            return BadRequest(new { message = "Название класса обязательно" });
        if (update.Year < 2000 || update.Year > 2100)
            return BadRequest(new { message = "Некорректный год" });
        if (await _context.Classes.AnyAsync(c => c.Id != id && c.Name == update.Name && c.Year == update.Year))
            return BadRequest(new { message = "Класс с таким названием и годом уже существует" });

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

        if (await _context.Students.AnyAsync(s => s.ClassId == id))
            return BadRequest(new { message = "Нельзя удалить класс: в нём есть ученики" });
        if (await _context.Schedules.AnyAsync(s => s.ClassId == id))
            return BadRequest(new { message = "Нельзя удалить класс: есть записи в расписании" });
        if (await _context.TeacherSubjects.AnyAsync(ts => ts.ClassId == id))
            return BadRequest(new { message = "Нельзя удалить класс: есть назначения учителей" });
        if (await _context.Homeworks.AnyAsync(h => h.ClassId == id))
            return BadRequest(new { message = "Нельзя удалить класс: есть домашние задания" });

        _context.Classes.Remove(cls);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("classes/{id:int}/students")]
    public async Task<IActionResult> GetClassStudents(int id)
    {
        var students = await _context.Students
            .Include(s => s.User)
            .OrderBy(s => s.User!.FullName)
            .Select(s => new
            {
                s.Id,
                Name = s.User != null ? s.User.FullName : ("Ученик #" + s.Id),
                s.ClassId,
                InClass = s.ClassId == id
            })
            .ToListAsync();
        return Ok(students);
    }

    [HttpPost("classes/{id:int}/students/{studentId:int}")]
    public async Task<IActionResult> AddStudentToClass(int id, int studentId)
    {
        if (!await _context.Classes.AnyAsync(c => c.Id == id))
            return NotFound(new { message = "Класс не найден" });
        var student = await _context.Students.FindAsync(studentId);
        if (student == null) return NotFound(new { message = "Ученик не найден" });
        if (student.ClassId == id)
            return BadRequest(new { message = "Ученик уже находится в этом классе" });

        student.ClassId = id;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpDelete("classes/{id:int}/students/{studentId:int}")]
    public async Task<IActionResult> RemoveStudentFromClass(int id, int studentId)
    {
        var student = await _context.Students
            .FirstOrDefaultAsync(s => s.Id == studentId && s.ClassId == id);
        if (student == null) return NotFound(new { message = "Ученик не найден в этом классе" });
        student.ClassId = null;
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("subjects")]
    public async Task<IActionResult> GetSubjects()
        => Ok(await _context.Subjects.AsNoTracking()
            .Select(s => new { s.Id, s.Name, s.Description }).ToListAsync());

    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject([FromBody] Subject s)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (string.IsNullOrWhiteSpace(s.Name))
            return BadRequest(new { message = "Название предмета обязательно" });
        if (await _context.Subjects.AnyAsync(x => x.Name == s.Name))
            return BadRequest(new { message = "Предмет с таким названием уже существует" });

        _context.Subjects.Add(s);
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.Name, s.Description });
    }

    [HttpPut("subjects/{id:int}")]
    public async Task<IActionResult> UpdateSubject(int id, [FromBody] Subject update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var s = await _context.Subjects.FindAsync(id);
        if (s == null) return NotFound();
        if (string.IsNullOrWhiteSpace(update.Name))
            return BadRequest(new { message = "Название предмета обязательно" });
        if (await _context.Subjects.AnyAsync(x => x.Id != id && x.Name == update.Name))
            return BadRequest(new { message = "Предмет с таким названием уже существует" });

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

        if (await _context.Grades.AnyAsync(g => g.SubjectId == id))
            return BadRequest(new { message = "Нельзя удалить предмет: по нему есть оценки" });
        if (await _context.Attendances.AnyAsync(a => a.SubjectId == id))
            return BadRequest(new { message = "Нельзя удалить предмет: по нему есть посещаемость" });
        if (await _context.Homeworks.AnyAsync(h => h.SubjectId == id))
            return BadRequest(new { message = "Нельзя удалить предмет: по нему есть домашние задания" });
        if (await _context.Schedules.AnyAsync(sc => sc.SubjectId == id))
            return BadRequest(new { message = "Нельзя удалить предмет: он используется в расписании" });
        if (await _context.TeacherSubjects.AnyAsync(ts => ts.SubjectId == id))
            return BadRequest(new { message = "Нельзя удалить предмет: есть назначения учителей" });

        _context.Subjects.Remove(s);
        await _context.SaveChangesAsync();
        return Ok();
    }

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
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (t.UserId.HasValue)
        {
            if (!await _context.Users.AnyAsync(u => u.Id == t.UserId))
                return BadRequest(new { message = "Пользователь не найден" });
            if (await _context.Teachers.AnyAsync(x => x.UserId == t.UserId))
                return BadRequest(new { message = "Этот пользователь уже является учителем" });
        }

        _context.Teachers.Add(t);
        await _context.SaveChangesAsync();
        return Ok(new { t.Id, t.UserId, t.Phone });
    }

    [HttpPut("teachers/{id:int}")]
    public async Task<IActionResult> UpdateTeacher(int id, [FromBody] Teacher update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var t = await _context.Teachers.FindAsync(id);
        if (t == null) return NotFound();

        if (update.UserId.HasValue)
        {
            if (!await _context.Users.AnyAsync(u => u.Id == update.UserId))
                return BadRequest(new { message = "Пользователь не найден" });
            if (await _context.Teachers.AnyAsync(x => x.Id != id && x.UserId == update.UserId))
                return BadRequest(new { message = "Этот пользователь уже является учителем" });
        }

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

        if (await _context.Schedules.AnyAsync(s => s.TeacherId == id))
            return BadRequest(new { message = "Нельзя удалить учителя: у него есть уроки в расписании" });
        if (await _context.Grades.AnyAsync(g => g.TeacherId == id))
            return BadRequest(new { message = "Нельзя удалить учителя: он выставлял оценки" });
        if (await _context.Homeworks.AnyAsync(h => h.TeacherId == id))
            return BadRequest(new { message = "Нельзя удалить учителя: у него есть домашние задания" });
        if (await _context.TeacherSubjects.AnyAsync(ts => ts.TeacherId == id))
            return BadRequest(new { message = "Нельзя удалить учителя: есть назначения предметов" });

        _context.Teachers.Remove(t);
        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("teacher-subjects")]
    public async Task<IActionResult> CreateTeacherSubject([FromBody] TeacherSubject ts)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (ts.TeacherId.HasValue && !await _context.Teachers.AnyAsync(t => t.Id == ts.TeacherId))
            return BadRequest(new { message = "Учитель не найден" });
        if (ts.SubjectId.HasValue && !await _context.Subjects.AnyAsync(s => s.Id == ts.SubjectId))
            return BadRequest(new { message = "Предмет не найден" });
        if (ts.ClassId.HasValue && !await _context.Classes.AnyAsync(c => c.Id == ts.ClassId))
            return BadRequest(new { message = "Класс не найден" });

        if (await _context.TeacherSubjects.AnyAsync(x =>
            x.TeacherId == ts.TeacherId && x.SubjectId == ts.SubjectId && x.ClassId == ts.ClassId))
            return BadRequest(new { message = "Такая связь уже существует" });

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
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (s.UserId.HasValue && !await _context.Users.AnyAsync(u => u.Id == s.UserId))
            return BadRequest(new { message = "Пользователь не найден" });
        if (s.ClassId.HasValue && !await _context.Classes.AnyAsync(c => c.Id == s.ClassId))
            return BadRequest(new { message = "Класс не найден" });

        _context.Students.Add(s);
        await _context.SaveChangesAsync();
        return Ok(new { s.Id, s.UserId, s.ClassId, s.BirthDate });
    }

    [HttpPut("students/{id:int}")]
    public async Task<IActionResult> UpdateStudent(int id, [FromBody] Student update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var s = await _context.Students.FindAsync(id);
        if (s == null) return NotFound();

        if (update.UserId.HasValue && !await _context.Users.AnyAsync(u => u.Id == update.UserId))
            return BadRequest(new { message = "Пользователь не найден" });
        if (update.ClassId.HasValue && !await _context.Classes.AnyAsync(c => c.Id == update.ClassId))
            return BadRequest(new { message = "Класс не найден" });

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

        if (await _context.Grades.AnyAsync(g => g.StudentId == id))
            return BadRequest(new { message = "Нельзя удалить ученика: есть оценки" });
        if (await _context.Attendances.AnyAsync(a => a.StudentId == id))
            return BadRequest(new { message = "Нельзя удалить ученика: есть посещаемость" });
        if (await _context.HomeworkStatuses.AnyAsync(h => h.StudentId == id))
            return BadRequest(new { message = "Нельзя удалить ученика: есть статусы домашних заданий" });
        if (await _context.ParentStudents.AnyAsync(ps => ps.StudentId == id))
            return BadRequest(new { message = "Нельзя удалить ученика: к нему привязаны родители" });

        _context.Students.Remove(s);
        await _context.SaveChangesAsync();
        return Ok();
    }

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
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (p.UserId.HasValue && !await _context.Users.AnyAsync(u => u.Id == p.UserId))
            return BadRequest(new { message = "Пользователь не найден" });

        _context.Parents.Add(p);
        await _context.SaveChangesAsync();
        return Ok(new { p.Id, p.UserId, p.Phone });
    }

    [HttpPut("parents/{id:int}")]
    public async Task<IActionResult> UpdateParent(int id, [FromBody] Parent update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var p = await _context.Parents.FindAsync(id);
        if (p == null) return NotFound();

        if (update.UserId.HasValue && !await _context.Users.AnyAsync(u => u.Id == update.UserId))
            return BadRequest(new { message = "Пользователь не найден" });

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

        if (await _context.ParentStudents.AnyAsync(ps => ps.ParentId == id))
            return BadRequest(new { message = "Нельзя удалить родителя: есть связанные ученики" });

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
        if (!await _context.Parents.AnyAsync(p => p.Id == parentId))
            return BadRequest(new { message = "Родитель не найден" });
        if (!await _context.Students.AnyAsync(s => s.Id == studentId))
            return BadRequest(new { message = "Ученик не найден" });

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
        if (link == null) return NotFound(new { message = "Связь не найдена" });
        _context.ParentStudents.Remove(link);
        await _context.SaveChangesAsync();
        return Ok();
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

    [HttpPost("quarters")]
    public async Task<IActionResult> CreateQuarter([FromBody] Quarter req)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (req == null) return BadRequest(new { message = "Пустой запрос" });
        if (req.QuarterNumber < 1 || req.QuarterNumber > 4)
            return BadRequest(new { message = "Номер четверти должен быть 1..4" });
        if (req.DateStart >= req.DateEnd)
            return BadRequest(new { message = "Дата начала должна быть раньше окончания" });

        if (await _context.Quarters.AnyAsync(q =>
                q.AcademicYear == req.AcademicYear && q.QuarterNumber == req.QuarterNumber))
            return BadRequest(new { message = "Такая четверть уже существует" });

        if (await _context.Quarters.AnyAsync(q =>
                q.AcademicYear == req.AcademicYear &&
                q.DateStart <= req.DateEnd && q.DateEnd >= req.DateStart))
            return BadRequest(new { message = "Период пересекается с другой четвертью этого учебного года" });

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
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var q = await _context.Quarters.FindAsync(id);
        if (q == null) return NotFound();

        if (req.AcademicYear > 0) q.AcademicYear = req.AcademicYear;
        if (req.QuarterNumber > 0) q.QuarterNumber = req.QuarterNumber;
        if (req.DateStart != default) q.DateStart = req.DateStart;
        if (req.DateEnd != default) q.DateEnd = req.DateEnd;

        if (q.QuarterNumber < 1 || q.QuarterNumber > 4)
            return BadRequest(new { message = "Номер четверти должен быть 1..4" });
        if (q.DateStart >= q.DateEnd)
            return BadRequest(new { message = "Дата начала должна быть раньше окончания" });

        if (await _context.Quarters.AnyAsync(x =>
                x.Id != id && x.AcademicYear == q.AcademicYear && x.QuarterNumber == q.QuarterNumber))
            return BadRequest(new { message = "Такая четверть уже существует" });

        if (await _context.Quarters.AnyAsync(x =>
                x.Id != id && x.AcademicYear == q.AcademicYear &&
                x.DateStart <= q.DateEnd && x.DateEnd >= q.DateStart))
            return BadRequest(new { message = "Период пересекается с другой четвертью этого учебного года" });

        await _context.SaveChangesAsync();
        return Ok(new { q.Id });
    }

    [HttpDelete("quarters/{id:int}")]
    public async Task<IActionResult> DeleteQuarter(int id)
    {
        var q = await _context.Quarters.FindAsync(id);
        if (q == null) return NotFound();

        if (await _context.Schedules.AnyAsync(s => s.QuarterId == id))
            return BadRequest(new { message = "Нельзя удалить четверть: есть записи в расписании" });

        _context.Quarters.Remove(q);
        await _context.SaveChangesAsync();
        return Ok();
    }

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
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (s.QuarterId <= 0) return BadRequest(new { message = "Укажите четверть" });
        if (!await _context.Quarters.AnyAsync(q => q.Id == s.QuarterId))
            return BadRequest(new { message = "Четверть не найдена" });
        if (!s.ClassId.HasValue || !await _context.Classes.AnyAsync(c => c.Id == s.ClassId))
            return BadRequest(new { message = "Класс не найден" });
        if (!s.SubjectId.HasValue || !await _context.Subjects.AnyAsync(sub => sub.Id == s.SubjectId))
            return BadRequest(new { message = "Предмет не найден" });
        if (!s.TeacherId.HasValue || !await _context.Teachers.AnyAsync(t => t.Id == s.TeacherId))
            return BadRequest(new { message = "Учитель не найден" });

        if (s.DayOfWeek < 1 || s.DayOfWeek > 6)
            return BadRequest(new { message = "Некорректный день недели" });
        if (s.LessonNumber < 1 || s.LessonNumber > 8)
            return BadRequest(new { message = "Некорректный номер урока" });

        var classConflict = await _context.Schedules.AnyAsync(x =>
            x.QuarterId == s.QuarterId && x.ClassId == s.ClassId &&
            x.DayOfWeek == s.DayOfWeek && x.LessonNumber == s.LessonNumber);
        if (classConflict)
            return BadRequest(new { message = "У класса уже есть урок в этот слот" });

        var teacherConflict = await _context.Schedules
            .Include(x => x.Class)
            .Include(x => x.Teacher).ThenInclude(t => t!.User)
            .FirstOrDefaultAsync(x => x.QuarterId == s.QuarterId
                     && x.TeacherId == s.TeacherId
                     && x.DayOfWeek == s.DayOfWeek
                     && x.LessonNumber == s.LessonNumber
                     && x.ClassId != s.ClassId);

        if (teacherConflict != null)
        {
            var teacherName = teacherConflict.Teacher?.User?.FullName ?? ("Учитель #" + s.TeacherId);
            var className = teacherConflict.Class?.Name ?? ("Класс #" + teacherConflict.ClassId);
            return BadRequest(new { message = $"{teacherName} уже ведёт урок в «{className}» в {DayNamesShort[s.DayOfWeek]}, урок {s.LessonNumber}" });
        }

        _context.Schedules.Add(s);
        await _context.SaveChangesAsync();
        return Ok(new { s.Id });
    }

    [HttpPut("schedule/{id:int}")]
    public async Task<IActionResult> UpdateSchedule(int id, [FromBody] Schedule update)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var s = await _context.Schedules.FindAsync(id);
        if (s == null) return NotFound();

        if (update.QuarterId > 0) s.QuarterId = update.QuarterId;
        s.ClassId = update.ClassId;
        s.SubjectId = update.SubjectId;
        s.TeacherId = update.TeacherId;
        s.DayOfWeek = update.DayOfWeek;
        s.LessonNumber = update.LessonNumber;
        s.Cabinet = update.Cabinet;

        if (s.DayOfWeek < 1 || s.DayOfWeek > 6)
            return BadRequest(new { message = "Некорректный день недели" });
        if (s.LessonNumber < 1 || s.LessonNumber > 8)
            return BadRequest(new { message = "Некорректный номер урока" });

        var classConflict = await _context.Schedules.AnyAsync(x =>
            x.Id != s.Id && x.QuarterId == s.QuarterId && x.ClassId == s.ClassId &&
            x.DayOfWeek == s.DayOfWeek && x.LessonNumber == s.LessonNumber);
        if (classConflict)
            return BadRequest(new { message = "У класса уже есть урок в этот слот" });

        if (s.TeacherId.HasValue && s.ClassId.HasValue)
        {
            var conflict = await _context.Schedules
                .Include(x => x.Class)
                .Include(x => x.Teacher).ThenInclude(t => t!.User)
                .Where(x => x.Id != s.Id
                         && x.QuarterId == s.QuarterId
                         && x.TeacherId == s.TeacherId
                         && x.DayOfWeek == s.DayOfWeek
                         && x.LessonNumber == s.LessonNumber
                         && x.ClassId != s.ClassId)
                .FirstOrDefaultAsync();

            if (conflict != null)
            {
                var teacherName = conflict.Teacher?.User?.FullName ?? ("Учитель #" + s.TeacherId);
                var className = conflict.Class?.Name ?? ("Класс #" + conflict.ClassId);
                return BadRequest(new { message = $"{teacherName} уже ведёт урок в «{className}» в {DayNamesShort[s.DayOfWeek]}, урок {s.LessonNumber}" });
            }
        }

        await _context.SaveChangesAsync();
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

    [HttpGet("schedule/week")]
    public async Task<IActionResult> GetScheduleWeek(
        [FromQuery] int classId,
        [FromQuery] int? quarterId,
        [FromQuery] DateTime? weekStart,
        [FromQuery] int? subjectId,
        [FromQuery] int? teacherId)
    {
        var baseDate = (weekStart ?? DateTime.UtcNow).Date;
        while (baseDate.DayOfWeek != DayOfWeek.Monday) baseDate = baseDate.AddDays(-1);

        Quarter? quarter = null;
        if (quarterId.HasValue)
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
        else
        {
            var dMonday = DateOnly.FromDateTime(baseDate);
            quarter = await _context.Quarters
                .Where(q => q.DateStart <= dMonday && q.DateEnd >= dMonday)
                .OrderBy(q => q.QuarterNumber)
                .FirstOrDefaultAsync();
        }

        DateTime? qStart = null, qEnd = null;
        if (quarter != null)
        {
            qStart = new DateTime(quarter.DateStart.Year, quarter.DateStart.Month, quarter.DateStart.Day);
            qEnd = new DateTime(quarter.DateEnd.Year, quarter.DateEnd.Month, quarter.DateEnd.Day);

            var minMon = qStart.Value;
            while (minMon.DayOfWeek != DayOfWeek.Monday) minMon = minMon.AddDays(-1);
            var maxMon = qEnd.Value;
            while (maxMon.DayOfWeek != DayOfWeek.Monday) maxMon = maxMon.AddDays(-1);
            if (baseDate < minMon) baseDate = minMon;
            if (baseDate > maxMon) baseDate = maxMon;
        }

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };

        var q = _context.Schedules
            .Include(sc => sc.Subject)
            .Include(sc => sc.Teacher).ThenInclude(t => t!.User)
            .Where(sc => sc.ClassId == classId);

        if (quarter != null)
            q = q.Where(sc => sc.QuarterId == quarter.Id);
        if (subjectId.HasValue)
            q = q.Where(sc => sc.SubjectId == subjectId.Value);
        if (teacherId.HasValue)
            q = q.Where(sc => sc.TeacherId == teacherId.Value);

        var schedules = await q.ToListAsync();

        var days = new List<object>();
        for (int i = 0; i < 6; i++)
        {
            var date = baseDate.AddDays(i);
            var dow = i + 1;
            bool inQuarter = true;
            if (qStart.HasValue && date < qStart.Value) inQuarter = false;
            if (qEnd.HasValue && date > qEnd.Value) inQuarter = false;

            var lessons = new List<object>();
            if (inQuarter)
            {
                foreach (var sc in schedules.Where(x => x.DayOfWeek == dow).OrderBy(x => x.LessonNumber))
                {
                    lessons.Add(new
                    {
                        lessonNumber = sc.LessonNumber,
                        subjectId = sc.SubjectId,
                        subjectName = sc.Subject?.Name,
                        teacherId = sc.TeacherId,
                        teacherName = sc.Teacher?.User?.FullName,
                        cabinet = sc.Cabinet
                    });
                }
            }
            days.Add(new { date, dayOfWeek = dow, dayName = dayNames[dow], inQuarter, lessons });
        }

        return Ok(new
        {
            classId,
            subjectId,
            teacherId,
            quarterId = quarter?.Id,
            quarterName = quarter?.Name,
            quarterStart = quarter?.DateStart,
            quarterEnd = quarter?.DateEnd,
            weekStart = baseDate,
            weekEnd = baseDate.AddDays(5),
            days
        });
    }

    public class BulkScheduleItem
    {
        public int DayOfWeek { get; set; }
        public int LessonNumber { get; set; }
        public int? SubjectId { get; set; }
        public int? TeacherId { get; set; }
        public string? Cabinet { get; set; }
    }

    public class BulkScheduleRequest
    {
        public int ClassId { get; set; }
        public int QuarterId { get; set; }
        public List<BulkScheduleItem> Items { get; set; } = new();
    }

    [HttpPost("schedule/bulk")]
    public async Task<IActionResult> SaveScheduleBulk([FromBody] BulkScheduleRequest req)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (req == null) return BadRequest(new { message = "Пустой запрос" });
        if (req.ClassId <= 0) return BadRequest(new { message = "Укажите класс" });
        if (req.QuarterId <= 0) return BadRequest(new { message = "Укажите четверть" });

        if (!await _context.Classes.AnyAsync(c => c.Id == req.ClassId))
            return BadRequest(new { message = "Класс не найден" });
        if (!await _context.Quarters.AnyAsync(q => q.Id == req.QuarterId))
            return BadRequest(new { message = "Четверть не найдена" });

        var errors = new List<string>();
        var seenSlots = new HashSet<(int day, int lesson)>();

        foreach (var item in req.Items)
        {
            if (item.DayOfWeek < 1 || item.DayOfWeek > 6)
                return BadRequest(new { message = $"Некорректный день недели: {item.DayOfWeek}" });
            if (item.LessonNumber < 1 || item.LessonNumber > 8)
                return BadRequest(new { message = $"Некорректный номер урока: {item.LessonNumber}" });
            if (item.SubjectId == null || item.TeacherId == null)
                return BadRequest(new { message = "Каждая запись должна содержать предмет и учителя" });

            if (!seenSlots.Add((item.DayOfWeek, item.LessonNumber)))
                errors.Add($"Дубликат: {DayNamesShort[item.DayOfWeek]}, урок {item.LessonNumber}");
        }

        var validItems = req.Items.Where(i => i.TeacherId.HasValue).ToList();
        var teacherIds = validItems.Select(i => i.TeacherId!.Value).Distinct().ToList();

        var conflicting = await _context.Schedules
            .Include(s => s.Class)
            .Include(s => s.Teacher).ThenInclude(t => t!.User)
            .Where(s => s.QuarterId == req.QuarterId
                     && s.ClassId != req.ClassId
                     && s.TeacherId != null
                     && teacherIds.Contains(s.TeacherId.Value)
                     && s.SubjectId != null)
            .Select(s => new
            {
                s.DayOfWeek,
                s.LessonNumber,
                TeacherId = s.TeacherId!.Value,
                TeacherName = s.Teacher!.User != null ? s.Teacher.User.FullName : ("Учитель #" + s.TeacherId),
                ClassName = s.Class != null ? s.Class.Name : ("Класс #" + s.ClassId)
            })
            .ToListAsync();

        foreach (var it in validItems)
        {
            var c = conflicting.FirstOrDefault(x =>
                x.TeacherId == it.TeacherId!.Value &&
                x.DayOfWeek == it.DayOfWeek &&
                x.LessonNumber == it.LessonNumber);
            if (c != null)
            {
                errors.Add($"{c.TeacherName}: {DayNamesShort[it.DayOfWeek]}, урок {it.LessonNumber} — уже ведёт в «{c.ClassName}»");
            }
        }

        if (errors.Count > 0)
            return BadRequest(new { message = "Обнаружены конфликты:\n• " + string.Join("\n• ", errors.Distinct()) });

        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            var existing = await _context.Schedules
                .Where(s => s.ClassId == req.ClassId && s.QuarterId == req.QuarterId)
                .ToListAsync();
            _context.Schedules.RemoveRange(existing);
            await _context.SaveChangesAsync();

            foreach (var item in req.Items)
            {
                _context.Schedules.Add(new Schedule
                {
                    ClassId = req.ClassId,
                    QuarterId = req.QuarterId,
                    SubjectId = item.SubjectId,
                    TeacherId = item.TeacherId,
                    DayOfWeek = item.DayOfWeek,
                    LessonNumber = item.LessonNumber,
                    Cabinet = string.IsNullOrWhiteSpace(item.Cabinet) ? null : item.Cabinet.Trim()
                });
            }
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

            return Ok(new { count = req.Items.Count });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Ошибка bulk-сохранения расписания ClassId={ClassId} QuarterId={QuarterId}", req.ClassId, req.QuarterId);
            return StatusCode(500, new { message = "Ошибка сохранения: " + ex.Message });
        }
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics([FromQuery] int? quarterId, [FromQuery] int? classId)
    {
        try
        {
            var totalUsers = await _context.Users.CountAsync();
            var totalStudents = await _context.Students.CountAsync();
            var totalTeachers = await _context.Teachers.CountAsync();
            var totalParents = await _context.Parents.CountAsync();
            var totalClasses = await _context.Classes.CountAsync();

            DateTime? from = null, toExclusive = null;
            if (quarterId.HasValue)
            {
                var q = await _context.Quarters.FindAsync(quarterId.Value);
                if (q != null)
                {
                    from = new DateTime(q.DateStart.Year, q.DateStart.Month, q.DateStart.Day);
                    toExclusive = new DateTime(q.DateEnd.Year, q.DateEnd.Month, q.DateEnd.Day).AddDays(1);
                }
            }

            var classesQuery = _context.Classes.AsNoTracking().AsQueryable();
            if (classId.HasValue) classesQuery = classesQuery.Where(c => c.Id == classId.Value);

            var classesRaw = await classesQuery
                .Select(c => new
                {
                    c.Id,
                    ClassName = c.Name,
                    StudentsCount = c.Students.Count,
                    Grades = c.Students.SelectMany(s => s.Grades).Select(g => new { g.Value, g.Date }).ToList(),
                    Attendances = c.Students.SelectMany(s => s.Attendances).Select(a => new { a.Status, a.Date }).ToList()
                })
                .ToListAsync();

            var avgByClass = classesRaw.Select(c => new
            {
                c.Id,
                c.ClassName,
                c.StudentsCount,
                AverageGrade = c.Grades.Any() ? c.Grades.Average(g => (double)g.Value) : 0.0
            }).ToList();

            var attendanceByClass = classesRaw.Select(c =>
            {
                var total = c.Attendances.Count;
                var present = c.Attendances.Count(a => a.Status != "н" && a.Status != "отсутствовал");
                return new
                {
                    c.Id,
                    c.ClassName,
                    Total = total,
                    Present = present,
                    Rate = total == 0 ? 100.0 : (double)present / total * 100
                };
            }).ToList();

            var allAtt = classesRaw.SelectMany(c => c.Attendances).ToList();
            int presentTotal = 0, absentTotal = 0, validTotal = 0;
            foreach (var a in allAtt)
            {
                var st = (a.Status ?? "").Trim().ToLower();
                if (st == "н" || st == "отсутствовал" || st == "отсутствовала") absentTotal++;
                else if (st == "уважительная") validTotal++;
                else presentTotal++;
            }

            var monthsRu = new[] { "Янв", "Фев", "Мар", "Апр", "Май", "Июн", "Июл", "Авг", "Сен", "Окт", "Ноя", "Дек" };
            var now = DateTime.UtcNow;
            var dynamics = new List<object>();
            var allGrades = classesRaw.SelectMany(c => c.Grades).ToList();
            for (int i = 5; i >= 0; i--)
            {
                var m = new DateTime(now.Year, now.Month, 1).AddMonths(-i);
                var next = m.AddMonths(1);
                var mg = allGrades.Where(g => g.Date >= m && g.Date < next).ToList();
                dynamics.Add(new
                {
                    label = monthsRu[m.Month - 1],
                    year = m.Year,
                    average = mg.Any() ? Math.Round(mg.Average(g => (double)g.Value), 2) : 0.0,
                    count = mg.Count
                });
            }

            var teacherActivityRaw = await _context.Grades
                .Where(g => g.TeacherId != null)
                .GroupBy(g => g.TeacherId!.Value)
                .Select(g => new { TeacherId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToListAsync();

            var teacherIds = teacherActivityRaw.Select(t => t.TeacherId).ToList();
            var teacherNames = await _context.Teachers
                .Where(t => teacherIds.Contains(t.Id))
                .Include(t => t.User)
                .ToDictionaryAsync(t => t.Id, t => t.User != null ? t.User.FullName : ("Учитель #" + t.Id));

            var teacherActivity = teacherActivityRaw.Select(t => new
            {
                teacherId = t.TeacherId,
                teacherName = teacherNames.TryGetValue(t.TeacherId, out var n) ? n : ("Учитель #" + t.TeacherId),
                count = t.Count
            }).ToList();

            return Ok(new
            {
                totalUsers,
                totalStudents,
                totalTeachers,
                totalParents,
                totalClasses,
                avgByClass,
                attendanceByClass,
                attendanceTotalPresent = presentTotal,
                attendanceTotalAbsent = absentTotal,
                attendanceTotalValid = validTotal,
                monthlyDynamics = dynamics,
                teacherActivity
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
            sb.Append('\uFEFF');
            sb.AppendLine("Класс;Учеников;Средний балл");
            foreach (var c in classes)
                sb.AppendLine($"{c.Name};{c.StudentsCount};{c.AverageGrade:F2}");

            return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", $"school_analytics_{DateTime.UtcNow:yyyyMMdd}.csv");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка экспорта аналитики");
            return StatusCode(500, new { message = "Ошибка экспорта" });
        }
    }

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

    internal async Task<object> BuildDiaryAsync(int classId, int? subjectId,
                                                int? studentId, int? quarterId,
                                                DateTime? weekStart)
    {
        var baseDate = (weekStart ?? DateTime.UtcNow).Date;
        while (baseDate.DayOfWeek != DayOfWeek.Monday) baseDate = baseDate.AddDays(-1);

        Quarter? quarter = null;
        if (quarterId.HasValue)
        {
            quarter = await _context.Quarters.FindAsync(quarterId.Value);
        }
        else
        {
            var dMonday = DateOnly.FromDateTime(baseDate);
            quarter = await _context.Quarters
                .Where(q => q.DateStart <= dMonday && q.DateEnd >= dMonday)
                .OrderBy(q => q.QuarterNumber)
                .FirstOrDefaultAsync();
        }

        DateTime? qStart = null, qEnd = null;
        if (quarter != null)
        {
            qStart = new DateTime(quarter.DateStart.Year, quarter.DateStart.Month, quarter.DateStart.Day);
            qEnd = new DateTime(quarter.DateEnd.Year, quarter.DateEnd.Month, quarter.DateEnd.Day);

            var minMon = qStart.Value;
            while (minMon.DayOfWeek != DayOfWeek.Monday) minMon = minMon.AddDays(-1);
            var maxMon = qEnd.Value;
            while (maxMon.DayOfWeek != DayOfWeek.Monday) maxMon = maxMon.AddDays(-1);
            if (baseDate < minMon) baseDate = minMon;
            if (baseDate > maxMon) baseDate = maxMon;
        }

        var toExclusive = baseDate.AddDays(7);

        var studentsQuery = _context.Students.Include(s => s.User).Where(s => s.ClassId == classId);
        if (studentId.HasValue) studentsQuery = studentsQuery.Where(s => s.Id == studentId);

        var students = await studentsQuery
            .OrderBy(s => s.User!.FullName)
            .Select(s => new { s.Id, Name = s.User!.FullName })
            .ToListAsync();

        var dayNames = new[] { "", "Понедельник", "Вторник", "Среда", "Четверг", "Пятница", "Суббота" };

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
                bool inQuarter = true;
                if (qStart.HasValue && date < qStart.Value) inQuarter = false;
                if (qEnd.HasValue && date > qEnd.Value) inQuarter = false;

                var daySchedules = schedules.Where(sc => sc.DayOfWeek == dow)
                    .OrderBy(sc => sc.LessonNumber).ToList();

                var lessons = new List<object>();
                if (inQuarter)
                {
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
                }
                days.Add(new { date, dayOfWeek = dow, dayName = dayNames[dow], inQuarter, lessons });
            }
            diaries.Add(new { studentId = st.Id, studentName = st.Name, days });
        }

        return new
        {
            classId,
            subjectId,
            quarterId = quarter?.Id,
            quarterName = quarter == null ? null : $"{quarter.QuarterNumber}-я четверть",
            quarterStart = quarter?.DateStart,
            quarterEnd = quarter?.DateEnd,
            weekStart = baseDate,
            weekEnd = baseDate.AddDays(5),
            diaries
        };
    }
}