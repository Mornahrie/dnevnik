using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using dnevnik.Data;
using dnevnik.Models;
using dnevnik.Services;

namespace dnevnik.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AuthController> _logger;

    public AuthController(ApplicationDbContext context,
                          IPasswordHasher passwordHasher,
                          ILogger<AuthController> logger)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        _logger.LogInformation("Попытка входа: Login={Login}, IP={IP}",
            request?.Login ?? "<null>", HttpContext.Connection.RemoteIpAddress);

        if (request is null
            || string.IsNullOrWhiteSpace(request.Login)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            _logger.LogWarning("Вход отклонён: пустые логин или пароль");
            return BadRequest(new { message = "Логин и пароль обязательны" });
        }

        try
        {
            var user = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.Login == request.Login);

            if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                _logger.LogWarning("Неверные учётные данные: Login={Login}", request.Login);
                return Unauthorized(new { message = "Неверный логин или пароль" });
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Заблокированная учётная запись: UserId={UserId}, Login={Login}",
                    user.Id, user.Login);
                return Unauthorized(new { message = "Учетная запись заблокирована" });
            }

            if (BCrypt.Net.BCrypt.PasswordNeedsRehash(user.PasswordHash, 12))
            {
                _logger.LogDebug("Рехеширование пароля для UserId={UserId}", user.Id);
                user.PasswordHash = _passwordHasher.Hash(request.Password);
                await _context.SaveChangesAsync();
            }

            var roleName = user.Role?.Name ?? "Unknown";

            var claims = new List<Claim>
            {
                new Claim("userId", user.Id.ToString()),
                new Claim("roleId", (user.RoleId ?? 0).ToString()),
                new Claim("login",  user.Login),
                new Claim("name",   user.FullName),
                new Claim("role",   roleName),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            _logger.LogDebug("Claims для UserId={UserId}: {Claims}",
                user.Id, string.Join("; ", claims.Select(c => $"{c.Type}={c.Value}")));

            var token = new JwtSecurityToken(
                issuer: AuthOptions.ISSUER,
                audience: AuthOptions.AUDIENCE,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: new SigningCredentials(
                    AuthOptions.GetSymmetricSecurityKey(),
                    SecurityAlgorithms.HmacSha256));

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            var redirectUrl = user.RoleId switch
            {
                1 => "/admin.html",
                2 => "/teacher.html",
                3 => "/student.html",
                4 => "/parent.html",
                _ => "/index.html"
            };

            _logger.LogInformation(
                "Успешный вход: UserId={UserId}, Login={Login}, RoleId={RoleId}, Role={Role}, Redirect={Url}",
                user.Id, user.Login, user.RoleId, roleName, redirectUrl);

            return Ok(new LoginResponse
            {
                Token = tokenString,
                Name = user.FullName,
                Role = roleName,
                RedirectUrl = redirectUrl
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке входа: Login={Login}", request.Login);
            return StatusCode(500, new { message = "Внутренняя ошибка сервера" });
        }
    }
}