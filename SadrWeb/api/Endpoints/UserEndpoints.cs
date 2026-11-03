using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record CreateUserRequest(string Username, string Password, string? Email, string? Role);
public record UpdateUserRequest(string Username, string? Email, string? Role, bool? EmailConfirmed);
public record ResetPasswordRequest(string NewPassword);

public static class UserEndpoints
{
    public static void MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("users").RequireAuthorization(Policies.Admin);

        group.MapGet("/", async (SadrDbContext db, string? q, CancellationToken ct) =>
        {
            var query = db.Users.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(u => u.Username.Contains(q) || u.Email!.Contains(q));
            var users = await query.OrderBy(u => u.Username)
                .Select(u => new UserDto(u.Id, u.Username, u.Email ?? "", u.Role ?? RoleConsts.User, u.EmailConfirmed))
                .ToListAsync(ct);
            return Results.Ok(users);
        });

        group.MapPost("/", async (CreateUserRequest body, SadrDbContext db, CancellationToken ct) =>
        {
            var username = (body.Username ?? "").Trim();
            if (username.Length == 0) return Results.BadRequest(new { message = "نام کاربری الزامی است." });
            if (string.IsNullOrWhiteSpace(body.Password) || body.Password.Length < 8)
                return Results.BadRequest(new { message = "رمز عبور باید حداقل ۸ نویسه باشد." });
            if (await db.Users.AnyAsync(u => u.Username == username, ct))
                return Results.Conflict(new { message = "این نام کاربری قبلاً ثبت شده است." });

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = body.Email ?? "",
                PasswordHash = PasswordHasher.Hash(body.Password),
                Role = NormalizeRole(body.Role),
                EmailConfirmed = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/users/{user.Id}",
                new UserDto(user.Id, user.Username, user.Email ?? "", user.Role ?? RoleConsts.User, user.EmailConfirmed));
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest body, SadrDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();

            var username = (body.Username ?? "").Trim();
            if (username.Length == 0) return Results.BadRequest(new { message = "نام کاربری الزامی است." });
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct))
                return Results.Conflict(new { message = "این نام کاربری قبلاً ثبت شده است." });

            user.Username = username;
            user.Email = body.Email ?? user.Email;
            user.Role = NormalizeRole(body.Role ?? user.Role);
            user.EmailConfirmed = body.EmailConfirmed ?? user.EmailConfirmed;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new UserDto(user.Id, user.Username, user.Email ?? "", user.Role ?? RoleConsts.User, user.EmailConfirmed));
        });

        group.MapPost("/{id:guid}/password", async (Guid id, ResetPasswordRequest body, SadrDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(body.NewPassword) || body.NewPassword.Length < 8)
                return Results.BadRequest(new { message = "رمز عبور باید حداقل ۸ نویسه باشد." });

            user.PasswordHash = PasswordHasher.Hash(body.NewPassword);
            user.RefreshToken = null;
            user.RefreshTokenExpiryTime = null;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, SadrDbContext db, CancellationToken ct) =>
        {
            if (http.User.Id() == id)
                return Results.BadRequest(new { message = "حذف حساب کاربری خودتان مجاز نیست." });

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (user is null) return Results.NotFound();
            if (user.Role == RoleConsts.Admin && await db.Users.CountAsync(u => u.Role == RoleConsts.Admin, ct) <= 1)
                return Results.BadRequest(new { message = "آخرین مدیر سیستم را نمی‌توان حذف کرد." });

            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static string NormalizeRole(string? role) =>
        role is RoleConsts.Admin or RoleConsts.Warehouse or RoleConsts.Sales or RoleConsts.User
            ? role : RoleConsts.User;
}
