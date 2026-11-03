using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

/// <summary>
/// The accounting chart of accounts: گروه حساب → حساب کل → حساب معین → حساب تفصیلی.
/// CRUD for each level lives in LookupEndpoints; this adds the tree view and the rules the
/// desktop editors enforce (a child level must point at a live parent).
/// </summary>
public static class AccountingEndpoints
{
    public static void MapAccountingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounting").WithTags("accounting");

        group.MapGet("/tree", async (SadrDbContext db, CancellationToken ct) =>
        {
            var groups = await db.AccountGroups.AsNoTracking().Where(g => !g.Deleted)
                .OrderBy(g => g.Code).ToListAsync(ct);
            var generals = await db.GeneralAccounts.AsNoTracking().Where(g => !g.Deleted)
                .OrderBy(g => g.Code).ToListAsync(ct);
            var subsidiaries = await db.SubSidiaryAccounts.AsNoTracking().Where(s => !s.Deleted)
                .OrderBy(s => s.Code).ToListAsync(ct);
            var details = await db.DetailAccounts.AsNoTracking().Where(d => !d.Deleted)
                .OrderBy(d => d.Code).ToListAsync(ct);

            var tree = groups.Select(group => new
            {
                group.Id, group.Name, group.Code, group.AccountType,
                GeneralAccounts = generals.Where(g => g.AccountGroupId == group.Id).Select(g => new
                {
                    g.Id, g.Name, g.Code, g.AccountType,
                    SubSidiaryAccounts = subsidiaries.Where(s => s.GeneralAccountId == g.Id).Select(s => new
                    {
                        s.Id, s.Name, s.Code, s.AccountType,
                        DetailAccounts = details.Where(d => d.SubSidiaryAccountId == s.Id)
                            .Select(d => new { d.Id, d.Name, d.Code, d.AccountType, d.CustomerProviderId })
                    })
                })
            });

            return Results.Ok(new
            {
                tree,
                orphans = new
                {
                    generalAccounts = generals.Count(g => !groups.Any(x => x.Id == g.AccountGroupId)),
                    subsidiaryAccounts = subsidiaries.Count(s => !generals.Any(x => x.Id == s.GeneralAccountId)),
                    detailAccounts = details.Count(d => !subsidiaries.Any(x => x.Id == d.SubSidiaryAccountId))
                }
            });
        });

        // --- حساب کل ---------------------------------------------------------------------
        group.MapPost("/general-accounts", async (GeneralAccount body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            if (body.AccountGroupId <= 0) return Results.BadRequest(new { message = "گروه حساب الزامی است." });
            if (!await db.AccountGroups.AnyAsync(g => g.Id == body.AccountGroupId && !g.Deleted, ct))
                return Results.BadRequest(new { message = "گروه حساب یافت نشد." });
            if (await CodeTakenAsync(db.GeneralAccounts.Where(g => !g.Deleted), body.Code, 0, ct))
                return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });

            db.GeneralAccounts.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/general-accounts/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        // --- حساب معین -------------------------------------------------------------------
        group.MapPost("/subsidiary-accounts", async (SubSidiaryAccount body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            if (body.GeneralAccountId <= 0) return Results.BadRequest(new { message = "حساب کل الزامی است." });
            if (!await db.GeneralAccounts.AnyAsync(g => g.Id == body.GeneralAccountId && !g.Deleted, ct))
                return Results.BadRequest(new { message = "حساب کل یافت نشد." });
            if (await CodeTakenAsync(db.SubSidiaryAccounts.Where(s => !s.Deleted), body.Code, 0, ct))
                return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });

            db.SubSidiaryAccounts.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/subsidiary-accounts/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        // --- حساب تفصیلی ------------------------------------------------------------------
        group.MapPost("/detail-accounts", async (DetailAccount body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            if (body.SubSidiaryAccountId <= 0) return Results.BadRequest(new { message = "حساب معین الزامی است." });
            if (!await db.SubSidiaryAccounts.AnyAsync(s => s.Id == body.SubSidiaryAccountId && !s.Deleted, ct))
                return Results.BadRequest(new { message = "حساب معین یافت نشد." });
            if (await CodeTakenAsync(db.DetailAccounts.Where(d => !d.Deleted), body.Code, 0, ct))
                return Results.Conflict(new { message = CodeRules.MsgDetailCodeDuplicate });

            db.DetailAccounts.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/detail-accounts/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        // --- profit & loss style summary per group ----------------------------------------
        group.MapGet("/summary", async (SadrDbContext db, CancellationToken ct) =>
        {
            // No journal-entry table exists in this database (accounting is driven by the
            // money, invoice and cheque ledgers), so the summary reports what is available:
            // the chart of accounts and how many detail accounts each group holds.
            var groups = await db.AccountGroups.AsNoTracking().Where(g => !g.Deleted).ToListAsync(ct);
            var generals = await db.GeneralAccounts.AsNoTracking().Where(g => !g.Deleted).ToListAsync(ct);
            var subsidiaries = await db.SubSidiaryAccounts.AsNoTracking().Where(s => !s.Deleted).ToListAsync(ct);
            var details = await db.DetailAccounts.AsNoTracking().Where(d => !d.Deleted).ToListAsync(ct);

            var rows = groups.Select(g =>
            {
                var generalIds = generals.Where(x => x.AccountGroupId == g.Id).Select(x => x.Id).ToList();
                var subIds = subsidiaries.Where(s => generalIds.Contains(s.GeneralAccountId)).Select(s => s.Id).ToList();
                return new
                {
                    g.Id, g.Name, g.Code, g.AccountType,
                    generalAccounts = generalIds.Count,
                    subsidiaryAccounts = subIds.Count,
                    detailAccounts = details.Count(d => subIds.Contains(d.SubSidiaryAccountId))
                };
            });

            return Results.Ok(rows);
        });
    }

    private static async Task<bool> CodeTakenAsync<T>(IQueryable<T> set, string? code, int ignoreId,
        CancellationToken ct) where T : class
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var normalized = CodeRules.NormalizeDigits(code);
        return await set.AnyAsync(e => EF.Property<string>(e, "Code") == normalized
                                       && EF.Property<int>(e, "Id") != ignoreId, ct);
    }
}
