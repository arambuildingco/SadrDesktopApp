using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;

namespace SadrWeb.Api.Infrastructure;

/// <summary>
/// Generic REST CRUD for the simple tables (lookups). Every entity in this project carries
/// the same audit + soft-delete columns as the desktop database, so one implementation
/// serves brands, units, currencies, warehouses, banks, accounting levels and so on.
///
/// Routes produced for "brands":
///   GET    /api/brands?q=&amp;page=1&amp;pageSize=50&amp;includeDeleted=false
///   GET    /api/brands/{id}
///   POST   /api/brands
///   PUT    /api/brands/{id}
///   DELETE /api/brands/{id}          → soft delete (Deleted = true)
///   POST   /api/brands/{id}/restore
/// </summary>
public static class Crud
{
    public record PagedResult<T>(IEnumerable<T> Items, int Total, int Page, int PageSize);

    public static void MapCrud<TEntity>(
        this IEndpointRouteBuilder app,
        string route,
        Func<IQueryable<TEntity>, string, IQueryable<TEntity>>? search = null,
        Func<IQueryable<TEntity>, IQueryable<TEntity>>? include = null)
        where TEntity : class
    {
        var group = app.MapGroup(route).WithTags(route.TrimStart('/'));

        group.MapGet("/", async (SadrDbContext db, string? q, int page = 1, int pageSize = 50,
            bool includeDeleted = false, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            IQueryable<TEntity> query = db.Set<TEntity>();
            if (include is not null) query = include(query);
            if (!includeDeleted) query = OnlyLive(query, typeof(TEntity));
            if (!string.IsNullOrWhiteSpace(q))
                query = search is not null ? search(query, q.Trim()) : DefaultSearch(query, q.Trim());

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderBy(e => EF.Property<int>(e, "Id"))
                .Skip((page - 1) * pageSize).Take(pageSize)
                .AsNoTracking()
                .ToListAsync(ct);

            return Results.Ok(new PagedResult<TEntity>(items, total, page, pageSize));
        }).WithSummary($"List {route.TrimStart('/')}");

        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var item = await db.Set<TEntity>().FindAsync(new object?[] { id }, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        });

        group.MapPost("/", async (TEntity body, SadrDbContext db, CancellationToken ct) =>
        {
            SetId(body, 0);
            db.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"{route}/{GetId(body)}", body);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, TEntity body, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Set<TEntity>().FindAsync(new object?[] { id }, ct);
            if (existing is null) return Results.NotFound();

            // Identity and creation audit must survive an update: a client that posts a
            // partial body (or omits them) must never wipe them. The key is copied onto the
            // body first, because SetValues refuses to change a key property.
            SetId(body, id);
            var preserved = new Dictionary<string, object?>
            {
                ["CreateDateTime"] = existing.GetType().GetProperty("CreateDateTime")?.GetValue(existing),
                ["CreateUserId"] = existing.GetType().GetProperty("CreateUserId")?.GetValue(existing),
                ["RecordUniqueId"] = existing.GetType().GetProperty("RecordUniqueId")?.GetValue(existing)
            };
            db.Entry(existing).CurrentValues.SetValues(body);
            foreach (var (name, value) in preserved)
                existing.GetType().GetProperty(name)?.SetValue(existing, value);
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Set<TEntity>().FindAsync(new object?[] { id }, ct);
            if (existing is null) return Results.NotFound();
            SoftDelete(existing);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPost("/{id:int}/restore", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Set<TEntity>().FindAsync(new object?[] { id }, ct);
            if (existing is null) return Results.NotFound();
            existing.GetType().GetProperty("Deleted")?.SetValue(existing, false);
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.Admin);
    }

    public static void SoftDelete(object entity)
    {
        var prop = entity.GetType().GetProperty("Deleted");
        if (prop is { CanWrite: true }) prop.SetValue(entity, true);
    }

    private static IQueryable<TEntity> OnlyLive<TEntity>(IQueryable<TEntity> query, Type type)
    {
        var prop = type.GetProperty("Deleted");
        if (prop is null || prop.PropertyType != typeof(bool)) return query;
        return query.Where(e => !EF.Property<bool>(e, "Deleted"));
    }

    /// <summary>Free-text search across the usual name/code/title columns of a table.</summary>
    public static IQueryable<TEntity> DefaultSearch<TEntity>(IQueryable<TEntity> query, string term)
    {
        var props = typeof(TEntity).GetProperties()
            .Where(p => p.PropertyType == typeof(string) &&
                        p.Name is "Name" or "Code" or "Title" or "FullName" or "Username"
                            or "FirstName" or "LastName" or "InvoiceNumber" or "Number")
            .Select(p => p.Name)
            .ToList();
        if (props.Count == 0) return query;

        // e => EF.Property<string>(e, name).Contains(term), combined with OrElse.
        // Contains translates to LIKE on every provider; building a DbFunctions.Like call by
        // reflection is fragile because Like is an extension method, not a static one.
        var entity = System.Linq.Expressions.Expression.Parameter(typeof(TEntity), "e");
        System.Linq.Expressions.Expression? body = null;
        var contains = typeof(string).GetMethod(nameof(string.Contains), new[] { typeof(string) })!;
        foreach (var name in props)
        {
            var prop = System.Linq.Expressions.Expression.Call(
                typeof(EF), nameof(EF.Property), new[] { typeof(string) }, entity,
                System.Linq.Expressions.Expression.Constant(name));
            var call = System.Linq.Expressions.Expression.Call(
                prop, contains, System.Linq.Expressions.Expression.Constant(term));
            body = body is null ? call : System.Linq.Expressions.Expression.OrElse(body, call);
        }
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<TEntity, bool>>(body!, entity);
        return query.Where(lambda);
    }

    public static int GetId(object entity) =>
        (int)(entity.GetType().GetProperty("Id")?.GetValue(entity) ?? 0);

    public static void SetId(object entity, int id)
    {
        var prop = entity.GetType().GetProperty("Id");
        if (prop is { CanWrite: true } && prop.PropertyType == typeof(int)) prop.SetValue(entity, id);
    }
}
