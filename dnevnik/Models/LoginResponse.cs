namespace dnevnik.Models;

public class LoginResponse
{
    public string Token { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string RedirectUrl { get; set; } = null!;
}