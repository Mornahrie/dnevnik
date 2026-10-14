using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using dnevnik.Services;
using dnevnik.Data;
using Microsoft.EntityFrameworkCore;

IdentityModelEventSource.ShowPII = true;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection"))));

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler =
            System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition =
            System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy =
            System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;

        options.UseSecurityTokenValidators = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = AuthOptions.ISSUER,

            ValidateAudience = true,
            ValidAudience = AuthOptions.AUDIENCE,

            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,

            IssuerSigningKey = AuthOptions.GetSymmetricSecurityKey(),
            ValidateIssuerSigningKey = true,

            RoleClaimType = "role",
            NameClaimType = "name"
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var auth = ctx.Request.Headers["Authorization"].ToString();

                if (!string.IsNullOrEmpty(auth) &&
                    auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var raw = auth.Substring("Bearer ".Length);

                    var cleaned = new string(raw
                        .Where(c => !char.IsWhiteSpace(c)
                                 && c != '\uFEFF'
                                 && c != '\u200B'
                                 && c != '\u200C'
                                 && c != '\u200D')
                        .ToArray());

                    ctx.Token = cleaned;

                    Console.WriteLine($"[JWT] Path={ctx.Request.Path}");
                    Console.WriteLine($"[JWT] Raw len={raw.Length}, Clean len={cleaned.Length}, Diff={raw.Length - cleaned.Length}");
                    Console.WriteLine($"[JWT] First 6 codes: {string.Join(",", raw.Take(6).Select(c => (int)c))}");
                    Console.WriteLine($"[JWT] Last 6 codes:  {string.Join(",", raw.Skip(raw.Length - 6).Select(c => (int)c))}");

                    var parts = cleaned.Split('.');
                    Console.WriteLine($"[JWT] Parts count = {parts.Length}");

                    if (parts.Length == 3)
                    {
                        try
                        {
                            var headerJson = DecodeBase64Url(parts[0]);
                            var payloadJson = DecodeBase64Url(parts[1]);
                            Console.WriteLine($"[JWT] Header JSON:  {headerJson}");
                            Console.WriteLine($"[JWT] Payload JSON: {payloadJson}");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[JWT] Manual decode FAILED: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[JWT] Path={ctx.Request.Path} Authorization=<нет или не Bearer>");
                }

                return Task.CompletedTask;
            },

            OnAuthenticationFailed = ctx =>
            {
                Console.WriteLine($"[JWT FAIL] {ctx.Exception.GetType().Name}: {ctx.Exception.Message}");
                return Task.CompletedTask;
            },

            OnTokenValidated = ctx =>
            {
                var claims = string.Join("; ",
                    ctx.Principal!.Claims.Select(c => $"{c.Type}={c.Value}"));
                Console.WriteLine($"[JWT OK] {claims}");

                var rolesToCheck = new[] { "Администратор", "Admin", "Учитель", "Teacher",
                                           "Ученик", "Учащийся", "Student",
                                           "Родитель", "Parent" };
                var roleHits = string.Join(", ",
                    rolesToCheck.Select(r => $"{r}={ctx.Principal!.IsInRole(r)}"));
                Console.WriteLine($"[JWT ROLES] {roleHits}");

                return Task.CompletedTask;
            },

            OnChallenge = ctx =>
            {
                Console.WriteLine($"[JWT CHALLENGE] Path={ctx.Request.Path}, " +
                                  $"Error={ctx.Error}, Desc={ctx.ErrorDescription}");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseCors("AllowAll");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

static string DecodeBase64Url(string s)
{
    s = s.Replace('-', '+').Replace('_', '/');
    switch (s.Length % 4)
    {
        case 2: s += "=="; break;
        case 3: s += "="; break;
    }
    var bytes = Convert.FromBase64String(s);
    return System.Text.Encoding.UTF8.GetString(bytes);
}