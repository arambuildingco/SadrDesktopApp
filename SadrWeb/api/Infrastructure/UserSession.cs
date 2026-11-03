using System.Security.Claims;

namespace SadrWeb.Api.Infrastructure;

/// <summary>
/// Resolves the signed-in user for audit stamping. The desktop app keeps this in a static
/// thread-local; a web request has its own scope, so it is read from the JWT claims.
/// </summary>
public static class UserSession
{
    public static Guid? Resolve(IHttpContextAccessor? accessor)
    {
        var principal = accessor?.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true) return null;
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    public static Guid? CurrentUserId(IHttpContextAccessor? accessor) => Resolve(accessor);

    public static string Role(IHttpContextAccessor? accessor) =>
        accessor?.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? "";
}

/// <summary>Authorization policy names used by the endpoints.</summary>
public static class Policies
{
    public const string Admin = "Admin";
    public const string Warehouse = "Warehouse";    // Admin + Warehouse
    public const string Invoices = "Invoices";      // Admin + Sales (+User, see below)
    public const string AnyUser = "AnyUser";
}
