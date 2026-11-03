using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Infrastructure;

/// <summary>
/// First-run seed data: the same defaults the desktop app creates (currency, roles catalog,
/// base units, the default warehouse) plus the first administrator account.
/// </summary>
public static class SeedData
{
    public static async Task EnsureAsync(SadrDbContext db, IConfiguration config, ILogger logger)
    {
        var now = DateTime.Now;

        if (!await db.Currencies.AnyAsync())
        {
            db.Currencies.Add(new Currency
            {
                Name = "ریال", Code = "IRR", Description = "واحد پول پیش‌فرض",
                RecordUniqueId = Guid.NewGuid(), CreateDateTime = now
            });
        }

        if (!await db.Roles.AnyAsync())
        {
            foreach (var (name, description) in new[]
                     {
                         (RoleConsts.Admin, "مدیر سیستم - دسترسی کامل"),
                         (RoleConsts.User, "کاربر عادی"),
                         (RoleConsts.Warehouse, "انباردار - ثبت و تأیید حواله‌های انبار"),
                         (RoleConsts.Sales, "فروشنده - ثبت فاکتورها")
                     })
            {
                db.Roles.Add(new Role
                {
                    Name = name, Description = description, IsActive = true,
                    RecordUniqueId = Guid.NewGuid(), CreateDateTime = now
                });
            }
        }

        if (!await db.Units.AnyAsync())
        {
            foreach (var name in new[] { "عدد", "کیلوگرم", "متر" })
                db.Units.Add(new Unit { Name = name, RecordUniqueId = Guid.NewGuid(), CreateDateTime = now });
        }

        await db.SaveChangesAsync();

        // Stock always lives in a warehouse: create the default one, like the desktop app.
        await DefaultWarehouse.EnsureAsync(db);

        if (!await db.Users.AnyAsync())
        {
            var username = config["Seed:AdminUsername"] ?? "admin";
            var password = config["Seed:AdminPassword"];
            var generated = string.IsNullOrWhiteSpace(password);
            if (generated) password = GeneratePassword();

            var admin = new User
            {
                Id = Guid.NewGuid(),
                Username = username,
                Email = config["Seed:AdminEmail"] ?? "admin@sadr.local",
                PasswordHash = PasswordHasher.Hash(password!),
                Role = RoleConsts.Admin,
                EmailConfirmed = true
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();

            if (generated)
            {
                var dir = Path.Combine(AppContext.BaseDirectory, "App_Data");
                Directory.CreateDirectory(dir);
                await File.WriteAllTextAsync(Path.Combine(dir, "admin-password.txt"),
                    $"username: {username}\npassword: {password}\n(change it after the first sign-in)\n");
                logger.LogWarning("Created the first administrator '{User}'. The generated password is in App_Data/admin-password.txt — set Seed:AdminPassword to choose your own.", username);
            }
            else
            {
                logger.LogInformation("Created the first administrator '{User}' from configuration.", username);
            }
        }
    }

    private static string GeneratePassword()
    {
        const string alphabet = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alphabet[b % alphabet.Length]).ToArray());
    }
}
