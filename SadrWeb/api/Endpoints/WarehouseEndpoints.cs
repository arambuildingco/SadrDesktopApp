using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record TransferInput(int ProductId, int Direction, decimal Quantity, DateTime? TransferDateG,
    int? WareHouseId, int? PersonId, string? Description);

public static class WarehouseEndpoints
{
    public static void MapWarehouseEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/warehouse").WithTags("warehouse");

        // --- transfer list ---------------------------------------------------------------
        group.MapGet("/transfers", async (SadrDbContext db, int? direction, bool? accepted, int? productId,
            int? warehouseId, int page = 1, int pageSize = 100, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var query = db.WarehouseTransfers.AsNoTracking().Where(t => !t.Deleted);
            if (direction is int d) query = query.Where(t => t.Direction == d);
            if (accepted is bool a) query = query.Where(t => t.Accepted == a);
            if (productId is int pid) query = query.Where(t => t.ProductId == pid);
            if (warehouseId is int wid) query = query.Where(t => t.WareHouseId == wid);

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(t => t.TransferDateG).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => new
                {
                    t.Id, t.ProductId, t.Direction, t.Quantity, t.TransferDate, t.TransferDateG,
                    t.Accepted, t.AcceptedByUserId, t.WareHouseId, t.InvoiceId, t.PersonId, t.Describtion,
                    ProductName = db.Products.Where(p => p.Id == t.ProductId).Select(p => p.Name).FirstOrDefault(),
                    WarehouseName = db.WareHouses.Where(w => w.Id == t.WareHouseId).Select(w => w.Name).FirstOrDefault(),
                    InvoiceNumber = db.Invoices.Where(i => i.Id == t.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(new { items = rows, total, page, pageSize });
        });

        // --- register a manual transfer --------------------------------------------------
        group.MapPost("/transfers", async (TransferInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (body.ProductId <= 0) return Results.BadRequest(new { message = "کالا الزامی است." });
            if (body.Quantity <= 0) return Results.BadRequest(new { message = "تعداد باید بزرگ‌تر از صفر باشد." });
            if (body.Direction is not (WarehouseTransfer.DirectionIn or WarehouseTransfer.DirectionOut))
                return Results.BadRequest(new { message = "نوع حواله نامعتبر است." });
            if (!await db.Products.AnyAsync(p => p.Id == body.ProductId && !p.Deleted, ct))
                return Results.BadRequest(new { message = "کالا یافت نشد." });

            var when = (body.TransferDateG ?? DateTime.Today).Date;
            var transfer = new WarehouseTransfer
            {
                ProductId = body.ProductId,
                Direction = body.Direction,
                Quantity = body.Quantity,
                TransferDateG = when,
                TransferDate = PersianDate.ToPersian(when),
                WareHouseId = body.WareHouseId is > 0 ? body.WareHouseId : await DefaultWarehouse.EnsureAsync(db),
                PersonId = body.PersonId is > 0 ? body.PersonId : null,
                Describtion = body.Description ?? "",
                Accepted = false,
                RecordUniqueId = Guid.NewGuid()
            };
            db.WarehouseTransfers.Add(transfer);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/warehouse/transfers/{transfer.Id}", transfer);
        }).RequireAuthorization(Policies.Warehouse);

        // --- edit: always returns the row to the warehouse queue -------------------------
        group.MapPut("/transfers/{id:int}", async (int id, TransferInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var transfer = await db.WarehouseTransfers.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (transfer is null) return Results.NotFound();
            if (body.Quantity <= 0) return Results.BadRequest(new { message = "تعداد باید بزرگ‌تر از صفر باشد." });

            transfer.ProductId = body.ProductId;
            transfer.Direction = body.Direction;
            transfer.Quantity = body.Quantity;
            transfer.TransferDateG = (body.TransferDateG ?? transfer.TransferDateG).Date;
            transfer.TransferDate = PersianDate.ToPersian(transfer.TransferDateG);
            transfer.WareHouseId = body.WareHouseId is > 0 ? body.WareHouseId : transfer.WareHouseId;
            transfer.PersonId = body.PersonId is > 0 ? body.PersonId : null;
            transfer.Describtion = body.Description ?? "";
            transfer.Accepted = false;
            transfer.AcceptedByUserId = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(transfer);
        }).RequireAuthorization(Policies.Warehouse);

        // --- accept / un-accept (warehouse keeper or admin only) -------------------------
        group.MapPost("/transfers/{id:int}/accept", async (int id, HttpContext http, bool? accept,
            SadrDbContext db, CancellationToken ct) =>
        {
            var role = http.User.Role();
            if (!Access.CanAcceptTransfers(role))
                return Results.Json(new
                {
                    message = "تأیید انجام حواله فقط توسط انباردار یا مدیر سیستم انجام می‌شود."
                }, statusCode: 403);

            var transfer = await db.WarehouseTransfers.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (transfer is null) return Results.NotFound();

            var value = accept ?? true;
            transfer.Accepted = value;
            transfer.AcceptedByUserId = value ? http.User.Id() : null;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                transfer.Id, transfer.Accepted,
                stock = await WarehouseStock.CurrentAsync(db, transfer.ProductId)
            });
        }).RequireAuthorization(Policies.Warehouse);

        group.MapDelete("/transfers/{id:int}", async (int id, HttpContext http, SadrDbContext db, CancellationToken ct) =>
        {
            var transfer = await db.WarehouseTransfers.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (transfer is null) return Results.NotFound();
            if (transfer.Accepted && !Access.CanAcceptTransfers(http.User.Role()))
                return Results.Json(new
                {
                    message = "حذف حواله‌های تأییدشده فقط توسط انباردار یا مدیر سیستم انجام می‌شود."
                }, statusCode: 403);

            transfer.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Warehouse);

        // --- stock of one product --------------------------------------------------------
        group.MapGet("/stock/{productId:int}", async (int productId, SadrDbContext db, int? warehouseId,
            CancellationToken ct) =>
        {
            var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, ct);
            if (product is null) return Results.NotFound();
            return Results.Ok(new
            {
                productId,
                product.Name,
                opening = product.InitialQuantity,
                stock = await WarehouseStock.CurrentAsync(db, productId, warehouseId),
                includingPending = product.InitialQuantity + await WarehouseStock.NetIncludingPendingAsync(db, productId, warehouseId),
                pendingTransfers = await db.WarehouseTransfers.CountAsync(t => !t.Deleted && !t.Accepted && t.ProductId == productId, ct)
            });
        });

        // --- stock report (all products, optionally per warehouse) -----------------------
        group.MapGet("/stock", async (SadrDbContext db, int? warehouseId, string? q, CancellationToken ct) =>
        {
            var products = db.Products.AsNoTracking().Where(p => !p.Deleted);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                products = products.Where(p => p.Name.Contains(term) || p.Code.Contains(term));
            }
            var rows = await products.OrderBy(p => p.Name)
                .Select(p => new { p.Id, p.Name, p.Code, p.InitialQuantity, p.MainUnitId })
                .ToListAsync(ct);

            var moves = await db.WarehouseTransfers.AsNoTracking()
                .Where(t => !t.Deleted && (warehouseId == null || t.WareHouseId == warehouseId))
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Accepted = g.Sum(t => t.Accepted ? (t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity) : 0m),
                    Pending = g.Sum(t => !t.Accepted ? (t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity) : 0m)
                })
                .ToDictionaryAsync(x => x.ProductId, x => x, ct);

            var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct);

            var items = rows.Select(p =>
            {
                var m = moves.GetValueOrDefault(p.Id);
                var accepted = m?.Accepted ?? 0m;
                var pending = m?.Pending ?? 0m;
                return new
                {
                    p.Id,
                    p.Name,
                    p.Code,
                    unit = units.GetValueOrDefault(p.MainUnitId, ""),
                    opening = p.InitialQuantity,
                    stock = p.InitialQuantity + accepted,
                    pending,
                    projected = p.InitialQuantity + accepted + pending
                };
            }).ToList();

            return Results.Ok(new
            {
                items,
                totals = new
                {
                    products = items.Count,
                    stock = items.Sum(i => i.stock),
                    projected = items.Sum(i => i.projected),
                    negative = items.Count(i => i.stock < 0)
                }
            });
        });
    }
}
