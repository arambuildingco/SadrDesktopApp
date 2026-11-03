using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

/// <summary>
/// The simple tables. Each one exposes the full REST surface from Crud.MapCrud:
/// list (paged + search), get, create, update, soft delete, restore.
/// </summary>
public static class LookupEndpoints
{
    public static void MapLookupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapCrud<Brand>("/api/brands");
        app.MapCrud<Unit>("/api/units");
        app.MapCrud<Currency>("/api/currencies");
        app.MapCrud<Role>("/api/roles");
        app.MapCrud<WareHouse>("/api/warehouses");
        app.MapCrud<Bank>("/api/banks");
        app.MapCrud<BankBranch>("/api/bank-branches");
        app.MapCrud<AttributeDef>("/api/attributes");
        app.MapCrud<ProductCategory>("/api/product-categories");
        app.MapCrud<ProjectCategory>("/api/project-categories");
        app.MapCrud<CustomersProvider>("/api/customers-providers");
        app.MapCrud<AccountGroup>("/api/account-groups");
        app.MapCrud<GeneralAccount>("/api/general-accounts");
        app.MapCrud<SubSidiaryAccount>("/api/subsidiary-accounts");
        app.MapCrud<DetailAccount>("/api/detail-accounts");
        app.MapCrud<InvoicePrintSetting>("/api/invoice-print-settings");
        app.MapCrud<ProjectTask>("/api/tasks");
        app.MapCrud<TaskReport>("/api/task-reports");

        // Prices are per product/unit/currency; searching them by product name is unhelpful,
        // so they get their own small surface with a product filter.
        var prices = app.MapGroup("/api/prices").WithTags("prices");
        prices.MapGet("/", async (SadrDbContext db, int? productId, bool activeOnly = false, CancellationToken ct = default) =>
        {
            var q = db.Prices.AsNoTracking().Where(p => !p.Deleted);
            if (productId is int pid) q = q.Where(p => p.ProductId == pid);
            if (activeOnly) q = q.Where(p => p.Status == 0 && p.EndDateG >= DateTime.Today);
            var rows = await q.OrderByDescending(p => p.SetDateG).ThenBy(p => p.Id).Take(500).ToListAsync(ct);
            return Results.Ok(rows);
        });
        prices.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
            await db.Prices.FindAsync(new object?[] { id }, ct) is { } p ? Results.Ok(p) : Results.NotFound());
        prices.MapPost("/", async (Price body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            db.Prices.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/prices/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);
        prices.MapPut("/{id:int}", async (int id, Price body, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Prices.FindAsync(new object?[] { id }, ct);
            if (existing is null) return Results.NotFound();
            var created = existing.CreateDateTime;
            db.Entry(existing).CurrentValues.SetValues(body);
            existing.Id = id;
            existing.CreateDateTime = created;
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);
        prices.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Prices.FindAsync(new object?[] { id }, ct);
            if (existing is null) return Results.NotFound();
            existing.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // Category attributes: which attribute belongs to which product category and whether
        // it is mandatory (IsForced) — drives the product attribute editor.
        var catAttribs = app.MapGroup("/api/category-attributes").WithTags("product-categories");
        catAttribs.MapGet("/", async (SadrDbContext db, int? categoryId, CancellationToken ct) =>
        {
            var q = db.CategoryAttributes.AsNoTracking().Where(a => !a.Deleted);
            if (categoryId is int cid) q = q.Where(a => a.CategoryId == cid);
            return Results.Ok(await q.OrderBy(a => a.Id).ToListAsync(ct));
        });
        catAttribs.MapPost("/", async (CategoryAttribute body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            db.CategoryAttributes.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/category-attributes/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);
        catAttribs.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var row = await db.CategoryAttributes.FindAsync(new object?[] { id }, ct);
            if (row is null) return Results.NotFound();
            row.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);
    }
}
