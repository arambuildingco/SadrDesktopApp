using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record PersonListItem(int Id, string FirstName, string? MiddleName, string? LastName, string? Code,
    string? Title, string? PhoneNumber, string? Email, string? Address, bool? IsActive, string FullName);

public record CompanyListItem(int Id, string FullName, string Code, string NationalId, string? Tel,
    string? Email, string? Address, int? ManagerId, string? Description);

/// <summary>
/// People and companies. Saving one mirrors it into CustomersProviders (so it can be the
/// party of an invoice) and keeps the auto DetailAccount in sync — the same behaviour as the
/// desktop editors, moved into the API so every client gets it for free.
/// </summary>
public static class PersonEndpoints
{
    public static void MapPersonEndpoints(this IEndpointRouteBuilder app)
    {
        var people = app.MapGroup("/api/people").WithTags("people");

        people.MapGet("/", async (SadrDbContext db, string? q, bool includeDeleted = false, CancellationToken ct = default) =>
        {
            var query = db.People.AsNoTracking().AsQueryable();
            if (!includeDeleted) query = query.Where(p => !p.Deleted);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(p => p.FirstName.Contains(term) || p.LastName.Contains(term)
                                         || (p.Code != null && p.Code.Contains(term))
                                         || (p.PhoneNumber != null && p.PhoneNumber.Contains(term)));
            }
            var rows = await query.OrderBy(p => p.Id)
                .Select(p => new PersonListItem(p.Id, p.FirstName, p.MiddleName, p.LastName, p.Code, p.Title,
                    p.PhoneNumber, p.Email, p.Address, p.IsActive,
                    (p.FirstName + " " + p.LastName).Trim()))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        people.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var p = await db.People.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (p is null) return Results.NotFound();
            var subsidiaryId = await DetailAccountService.GetCurrentSubSidiaryForPersonAsync(db, id);
            return Results.Ok(new { person = p, subsidiaryAccountId = subsidiaryId });
        });

        people.MapPost("/", async (Person body, SadrDbContext db, CancellationToken ct) =>
            await SavePersonAsync(db, body, ct) is { } error ? error : Results.Created($"/api/people/{body.Id}", body))
            .RequireAuthorization(Policies.AnyUser);

        people.MapPut("/{id:int}", async (int id, Person body, SadrDbContext db, int? subsidiaryAccountId, CancellationToken ct) =>
        {
            var existing = await db.People.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (existing is null) return Results.NotFound();

            body.Id = id;
            body.CreateDateTime = existing.CreateDateTime;
            body.CreateUserId = existing.CreateUserId;
            body.RecordUniqueId = existing.RecordUniqueId;
            db.Entry(existing).CurrentValues.SetValues(body);
            await db.SaveChangesAsync(ct);
            await CustomersProviderService.SyncPersonAsync(db, existing);
            await DetailAccountService.SyncPersonAsync(db, existing, subsidiaryAccountId);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        people.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var p = await db.People.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            p.Deleted = true;
            var cp = await db.CustomersProviders.FirstOrDefaultAsync(c => c.PersonId == id, ct);
            if (cp is not null) cp.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);

        var companies = app.MapGroup("/api/companies").WithTags("companies");

        companies.MapGet("/", async (SadrDbContext db, string? q, bool includeDeleted = false, CancellationToken ct = default) =>
        {
            var query = db.Companies.AsNoTracking().AsQueryable();
            if (!includeDeleted) query = query.Where(c => !c.Deleted);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(c => c.FullName.Contains(term) || c.Code.Contains(term) || c.NationalId.Contains(term));
            }
            var rows = await query.OrderBy(c => c.Id)
                .Select(c => new CompanyListItem(c.Id, c.FullName, c.Code, c.NationalId, c.Tel, c.Email,
                    c.Address, c.ManagerId, c.Description))
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        companies.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.Deleted, ct);
            if (c is null) return Results.NotFound();
            var subsidiaryId = await DetailAccountService.GetCurrentSubSidiaryForCompanyAsync(db, id);
            return Results.Ok(new { company = c, subsidiaryAccountId = subsidiaryId });
        });

        companies.MapPost("/", async (Company body, SadrDbContext db, CancellationToken ct) =>
            await SaveCompanyAsync(db, body, ct) is { } error ? error : Results.Created($"/api/companies/{body.Id}", body))
            .RequireAuthorization(Policies.AnyUser);

        companies.MapPut("/{id:int}", async (int id, Company body, SadrDbContext db, int? subsidiaryAccountId, CancellationToken ct) =>
        {
            var existing = await db.Companies.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existing is null) return Results.NotFound();

            body.Id = id;
            body.CreateDateTime = existing.CreateDateTime;
            body.CreateUserId = existing.CreateUserId;
            body.RecordUniqueId = existing.RecordUniqueId;
            db.Entry(existing).CurrentValues.SetValues(body);
            await db.SaveChangesAsync(ct);
            await CustomersProviderService.SyncCompanyAsync(db, existing);
            await DetailAccountService.SyncCompanyAsync(db, existing, subsidiaryAccountId);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        companies.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var c = await db.Companies.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            c.Deleted = true;
            var cp = await db.CustomersProviders.FirstOrDefaultAsync(x => x.CompanyId == id, ct);
            if (cp is not null) cp.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
    }

    private static async Task<IResult?> SavePersonAsync(SadrDbContext db, Person body, CancellationToken ct)
    {
        body.Id = 0;
        body.FirstName = (body.FirstName ?? "").Trim();
        body.LastName = (body.LastName ?? "").Trim();
        if (body.FirstName.Length == 0 && body.LastName.Length == 0)
            return Results.BadRequest(new { message = "نام شخص الزامی است." });

        if (!string.IsNullOrWhiteSpace(body.Code))
        {
            body.Code = CodeRules.NormalizeDigits(body.Code);
            if (await db.People.AnyAsync(p => !p.Deleted && p.Code == body.Code, ct))
                return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });
        }

        db.People.Add(body);
        await db.SaveChangesAsync(ct);
        await CustomersProviderService.SyncPersonAsync(db, body);
        return null;
    }

    private static async Task<IResult?> SaveCompanyAsync(SadrDbContext db, Company body, CancellationToken ct)
    {
        body.Id = 0;
        body.FullName = (body.FullName ?? "").Trim();
        if (body.FullName.Length == 0)
            return Results.BadRequest(new { message = "نام شرکت الزامی است." });

        body.Code = CodeRules.NormalizeDigits(body.Code ?? "");
        if (await db.Companies.AnyAsync(c => !c.Deleted && c.Code == body.Code && body.Code != "", ct))
            return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });

        // شناسه ملی is optional but must pass the checksum when provided.
        if (CodeRules.ValidateLegalNationalId(body.NationalId) is string legalError)
            return Results.BadRequest(new { message = legalError });

        db.Companies.Add(body);
        await db.SaveChangesAsync(ct);
        await CustomersProviderService.SyncCompanyAsync(db, body);
        return null;
    }
}
