using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SadrWeb.Api.Data;

namespace SadrWeb.Api.Infrastructure;

/// <summary>JWT issuing / refresh-token rotation for the REST API.</summary>
public class TokenService
{
    private readonly JwtOptions _options;
    public TokenService(JwtOptions options) => _options = options;

    public static IEnumerable<Claim> ClaimsFor(User u) => new[]
    {
        new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()),
        new Claim(ClaimTypes.Name, u.Username),
        new Claim(ClaimTypes.Email, u.Email ?? ""),
        new Claim(ClaimTypes.Role, string.IsNullOrWhiteSpace(u.Role) ? RoleConsts.User : u.Role)
    };

    public string CreateAccessToken(User user, out DateTime expires)
    {
        expires = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: ClaimsFor(user),
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Creates a refresh token on the user row, replacing any previous one.</summary>
    public (string token, DateTime expiry) IssueRefreshToken(User user)
    {
        var token = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        var expiry = DateTime.UtcNow.AddDays(_options.RefreshTokenDays);
        user.RefreshToken = token;
        user.RefreshTokenExpiryTime = expiry;
        return (token, expiry);
    }

    public static Guid? UserIdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}

public record JwtOptions
{
    public string Key { get; init; } = "";
    public string Issuer { get; init; } = "SadrWeb";
    public string Audience { get; init; } = "SadrClients";
    public int AccessTokenMinutes { get; init; } = 120;
    public int RefreshTokenDays { get; init; } = 14;

    /// <summary>Copies JWT settings from configuration, falling back to a generated dev key.</summary>
    public static JwtOptions From(IConfiguration config)
    {
        var section = config.GetSection("Jwt");
        var key = section["Key"];
        if (string.IsNullOrWhiteSpace(key))
        {
            // Deterministic per installation: generated once and stored beside the database so
            // tokens survive a restart without ever shipping a hard-coded secret.
            key = DevKey.LoadOrCreate();
        }
        return new JwtOptions
        {
            Key = key,
            Issuer = section["Issuer"] ?? "SadrWeb",
            Audience = section["Audience"] ?? "SadrClients",
            AccessTokenMinutes = int.TryParse(section["AccessTokenMinutes"], out var m) ? m : 120,
            RefreshTokenDays = int.TryParse(section["RefreshTokenDays"], out var d) ? d : 14
        };
    }
}

/// <summary>Creates (once) and reuses a local signing key file for development installs.</summary>
public static class DevKey
{
    private static string? _cached;

    public static string LoadOrCreate()
    {
        if (_cached is not null) return _cached;
        var dir = Path.Combine(AppContext.BaseDirectory, "App_Data");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "jwt.key");
        if (File.Exists(path))
        {
            _cached = File.ReadAllText(path).Trim();
            if (_cached.Length >= 32) return _cached;
        }
        _cached = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        File.WriteAllText(path, _cached);
        return _cached;
    }
}

/// <summary>Access checks shared by endpoints, mirroring the desktop UserSession flags.</summary>
public static class Access
{
    public static bool IsAdmin(string role) => role == RoleConsts.Admin;
    public static bool CanRegisterInvoices(string role) =>
        role is RoleConsts.Admin or RoleConsts.Sales or RoleConsts.User or "";
    public static bool CanManageWarehouse(string role) =>
        role is RoleConsts.Admin or RoleConsts.Warehouse or RoleConsts.User or "";
    public static bool CanAcceptTransfers(string role) => role is RoleConsts.Admin or RoleConsts.Warehouse or "";
}

/// <summary>Convenience helpers for the endpoint layer.</summary>
public static class HttpUser
{
    public static Guid? Id(this ClaimsPrincipal user) => TokenService.UserIdOf(user);
    public static string Role(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role) ?? RoleConsts.User;
    public static string Name(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? "";
}
