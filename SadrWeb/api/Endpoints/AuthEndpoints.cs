using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record LoginRequest(string Username, string Password);
public record RefreshRequest(string RefreshToken);
public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public record UserDto(Guid Id, string Username, string Email, string Role, bool EmailConfirmed);
public record LoginResponse(string AccessToken, DateTime ExpiresAt, string RefreshToken, UserDto User);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("auth");

        group.MapPost("/login", async (LoginRequest body, SadrDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            var name = body.Username?.Trim() ?? "";
            if (name.Length == 0 || string.IsNullOrEmpty(body.Password))
                return Results.BadRequest(new { message = "نام کاربری و رمز عبور الزامی است." });

            var user = await db.Users.FirstOrDefaultAsync(u => u.Username == name, ct);
            if (user is null || !PasswordHasher.Verify(body.Password, user.PasswordHash))
                return Results.Json(new { message = "نام کاربری یا رمز عبور نادرست است." }, statusCode: 401);

            var access = tokens.CreateAccessToken(user, out var expires);
            var (refresh, _) = tokens.IssueRefreshToken(user);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new LoginResponse(access, expires, refresh,
                new UserDto(user.Id, user.Username, user.Email ?? "", user.Role ?? RoleConsts.User, user.EmailConfirmed)));
        }).AllowAnonymous().WithSummary("ورود — access token + refresh token");

        group.MapPost("/refresh", async (RefreshRequest body, SadrDbContext db, TokenService tokens, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(body.RefreshToken))
                return Results.BadRequest(new { message = "refresh token لازم است." });

            var user = await db.Users.FirstOrDefaultAsync(u => u.RefreshToken == body.RefreshToken, ct);
            if (user is null || user.RefreshTokenExpiryTime is null || user.RefreshTokenExpiryTime < DateTime.UtcNow)
                return Results.Json(new { message = "refresh token منقضی یا نامعتبر است." }, statusCode: 401);

            var access = tokens.CreateAccessToken(user, out var expires);
            var (refresh, _) = tokens.IssueRefreshToken(user); // rotation
            await db.SaveChangesAsync(ct);
            return Results.Ok(new LoginResponse(access, expires, refresh,
                new UserDto(user.Id, user.Username, user.Email ?? "", user.Role ?? RoleConsts.User, user.EmailConfirmed)));
        }).AllowAnonymous().WithSummary("تمدید توکن دسترسی");

        group.MapPost("/logout", async (HttpContext http, SadrDbContext db, CancellationToken ct) =>
        {
            var id = http.User.Id();
            if (id is Guid uid)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
                if (user is not null)
                {
                    user.RefreshToken = null;
                    user.RefreshTokenExpiryTime = null;
                    await db.SaveChangesAsync(ct);
                }
            }
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        group.MapGet("/me", async (HttpContext http, SadrDbContext db, CancellationToken ct) =>
        {
            var id = http.User.Id();
            if (id is not Guid uid) return Results.Unauthorized();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid, ct);
            return user is null
                ? Results.NotFound()
                : Results.Ok(new UserDto(user.Id, user.Username, user.Email ?? "", user.Role ?? RoleConsts.User, user.EmailConfirmed));
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPost("/change-password", async (ChangePasswordRequest body, HttpContext http, SadrDbContext db, CancellationToken ct) =>
        {
            var id = http.User.Id();
            if (id is not Guid uid) return Results.Unauthorized();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid, ct);
            if (user is null) return Results.NotFound();

            if (!PasswordHasher.Verify(body.CurrentPassword, user.PasswordHash))
                return Results.BadRequest(new { message = "رمز عبور فعلی نادرست است." });
            if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < 8)
                return Results.BadRequest(new { message = "رمز عبور جدید باید حداقل ۸ نویسه باشد." });

            user.PasswordHash = PasswordHasher.Hash(body.NewPassword);
            user.RefreshToken = null; // force the other devices to sign in again
            user.RefreshTokenExpiryTime = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);
    }
}
