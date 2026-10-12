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

    public AuthController(ApplicationDbContext context, IPasswordHasher passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Login) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Логин и пароль обязательны" });
        }

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Login == request.Login);

        if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized(new { message = "Неверный логин или пароль" });
        }

        if (!user.IsActive)
        {
            return Unauthorized(new { message = "Учетная запись заблокирована" });
        }

        if (BCrypt.Net.BCrypt.PasswordNeedsRehash(user.PasswordHash, 12))
        {
            user.PasswordHash = _passwordHasher.Hash(request.Password);
            await _context.SaveChangesAsync();
        }

        var claims = new[]
        {
            new Claim("userId", user.Id.ToString()),
            new Claim("roleId", user.RoleId.ToString() ?? "0"),
            new Claim("login", user.Login),
            new Claim("name", user.FullName),
            new Claim(ClaimTypes.Role, user.Role?.Name ?? "Unknown")
        };

        var token = new JwtSecurityToken(
            issuer: AuthOptions.ISSUER,
            audience: AuthOptions.AUDIENCE,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: new SigningCredentials(
                AuthOptions.GetSymmetricSecurityKey(),
                SecurityAlgorithms.HmacSha256));

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        string redirectUrl = user.RoleId switch
        {
            1 => "/admin.html",
            2 => "/teacher.html",
            3 => "/student.html",
            4 => "/parent.html",
            _ => "/index.html"
        };

        return Ok(new LoginResponse
        {
            Token = tokenString,
            Name = user.FullName,
            Role = user.Role?.Name ?? "Пользователь",
            RedirectUrl = redirectUrl
        });
    }
}