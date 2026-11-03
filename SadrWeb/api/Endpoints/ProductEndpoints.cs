using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record ProductListItem(int Id, string Name, string Code, string Description, int MainCategoryId,
    string CategoryName, int MainUnitId, string UnitName, int? BrandId, string? BrandName,
    decimal InitialQuantity, decimal Stock, decimal? LastPrice);

public record ProductImageDto(int ProductFileId, int UploadedFileId, string OriginalName, string Url, long FileSize);
public record AttribValue(int AttributeId, int CategoryAttributeId, string Value);

public static class ProductEndpoints
{
    public static void MapProductEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/products").WithTags("products");

        // --- list with live stock and current price -------------------------------------
        group.MapGet("/", async (SadrDbContext db, string? q, int? categoryId, int? brandId,
            bool includeDeleted = false, int page = 1, int pageSize = 100, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var products = db.Products.AsNoTracking().AsQueryable();
            if (!includeDeleted) products = products.Where(p => !p.Deleted);
            if (categoryId is int cid) products = products.Where(p => p.MainCategoryId == cid);
            if (brandId is int bid) products = products.Where(p => p.BrandId == bid);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                products = products.Where(p => p.Name.Contains(term) || p.Code.Contains(term));
            }

            var total = await products.CountAsync(ct);

            // Accepted transfers per product (stock = opening + accepted in − accepted out)
            var moves = await db.WarehouseTransfers.AsNoTracking()
                .Where(t => !t.Deleted)
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    Net = g.Sum(t => t.Accepted
                        ? (t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity)
                        : 0m)
                })
                .ToDictionaryAsync(x => x.ProductId, x => x.Net, ct);

            var latestPrice = await db.Prices.AsNoTracking()
                .Where(p => !p.Deleted)
                .GroupBy(p => p.ProductId)
                .Select(g => new { ProductId = g.Key, Price = g.Max(p => p.PriceAmount) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Price, ct);

            var rows = await products.OrderBy(p => p.Id).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new
                {
                    p.Id, p.Name, p.Code, p.Description, p.MainCategoryId, p.MainUnitId, p.BrandId, p.InitialQuantity,
                    CategoryName = db.ProductCategories.Where(c => c.Id == p.MainCategoryId).Select(c => c.Name).FirstOrDefault(),
                    UnitName = db.Units.Where(u => u.Id == p.MainUnitId).Select(u => u.Name).FirstOrDefault(),
                    BrandName = db.Brands.Where(b => b.Id == p.BrandId).Select(b => b.Name).FirstOrDefault()
                })
                .ToListAsync(ct);

            var items = rows.Select(p => new ProductListItem(p.Id, p.Name, p.Code, p.Description, p.MainCategoryId,
                p.CategoryName ?? "", p.MainUnitId, p.UnitName ?? "", p.BrandId, p.BrandName,
                p.InitialQuantity, p.InitialQuantity + moves.GetValueOrDefault(p.Id),
                latestPrice.TryGetValue(p.Id, out var price) ? price : null));

            return Results.Ok(new { items, total, page, pageSize });
        });

        // --- one product with everything the editor needs -------------------------------
        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();

            var attribs = await db.ProductAttribs.AsNoTracking()
                .Where(a => a.ProductId == id && !a.Deleted).ToListAsync(ct);
            var extraCategories = await db.ProductExtraCategories.AsNoTracking()
                .Where(e => e.ProductId == id && !e.Deleted).ToListAsync(ct);
            var prices = await db.Prices.AsNoTracking()
                .Where(pr => pr.ProductId == id && !pr.Deleted).OrderByDescending(pr => pr.SetDateG).ToListAsync(ct);
            var images = await (from f in db.ProductFiles
                                join u in db.UploadedFiles on f.UploadedFileId equals u.Id
                                where f.ProductId == id && !f.Deleted
                                select new ProductImageDto(f.Id, u.Id, u.OriginalName, $"/api/files/{u.Id}", u.FileSize))
                .ToListAsync(ct);

            return Results.Ok(new
            {
                product = p,
                stock = await WarehouseStock.CurrentAsync(db, id),
                attributes = attribs,
                extraCategories,
                prices,
                images
            });
        });

        // --- create / update / delete ---------------------------------------------------
        group.MapPost("/", async (Product body, SadrDbContext db, CancellationToken ct) =>
        {
            if (await SaveNewAsync(db, body, ct) is { } error) return error;
            return Results.Created($"/api/products/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, Product body, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct);
            if (existing is null) return Results.NotFound();

            body.Name = (body.Name ?? "").Trim();
            if (body.Name.Length == 0) return Results.BadRequest(new { message = "نام کالا الزامی است." });
            if (body.MainCategoryId <= 0 || body.MainUnitId <= 0)
                return Results.BadRequest(new { message = "دسته‌بندی و واحد کالا الزامی است." });
            if (await db.Products.AnyAsync(p => !p.Deleted && p.Code == body.Code && p.Id != id, ct))
                return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });

            body.Id = id;
            body.CreateDateTime = existing.CreateDateTime;
            body.CreateUserId = existing.CreateUserId;
            body.RecordUniqueId = existing.RecordUniqueId;
            db.Entry(existing).CurrentValues.SetValues(body);
            await db.SaveChangesAsync(ct);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            p.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- images ---------------------------------------------------------------------
        group.MapGet("/{id:int}/images", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var images = await (from f in db.ProductFiles
                                join u in db.UploadedFiles on f.UploadedFileId equals u.Id
                                where f.ProductId == id && !f.Deleted
                                select new ProductImageDto(f.Id, u.Id, u.OriginalName, $"/api/files/{u.Id}", u.FileSize))
                .ToListAsync(ct);
            return Results.Ok(images);
        });

        group.MapPost("/{id:int}/images", async (int id, HttpRequest request, SadrDbContext db, FileStore store, CancellationToken ct) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id && !p.Deleted, ct)) return Results.NotFound();
            if (!request.HasFormContentType)
                return Results.BadRequest(new { message = "درخواست باید multipart/form-data باشد." });

            var form = await request.ReadFormAsync(ct);
            if (form.Files.Count == 0) return Results.BadRequest(new { message = "هیچ فایلی ارسال نشده است." });

            var added = new List<ProductImageDto>();
            var saved = new List<(UploadedFile file, string stored)>();
            try
            {
                foreach (var file in form.Files)
                {
                    await using var stream = file.OpenReadStream();
                    var uploaded = await store.SaveAsync(stream, file.FileName, ct);
                    db.UploadedFiles.Add(uploaded);
                    await db.SaveChangesAsync(ct);

                    var link = new ProductFile { ProductId = id, UploadedFileId = uploaded.Id, RecordUniqueId = Guid.NewGuid() };
                    db.ProductFiles.Add(link);
                    await db.SaveChangesAsync(ct);

                    saved.Add((uploaded, uploaded.FileName));
                    added.Add(new ProductImageDto(link.Id, uploaded.Id, uploaded.OriginalName, $"/api/files/{uploaded.Id}", uploaded.FileSize));
                }
            }
            catch (Exception ex)
            {
                // Roll back: drop the copied files and any rows written for this request.
                foreach (var (file, _) in saved) store.DeleteQuietly(file.FileName);
                return Results.BadRequest(new { message = ex.Message });
            }

            return Results.Ok(added);
        }).RequireAuthorization(Policies.AnyUser).DisableAntiforgery();

        group.MapDelete("/{id:int}/images/{productFileId:int}", async (int id, int productFileId, SadrDbContext db, CancellationToken ct) =>
        {
            var link = await db.ProductFiles.FirstOrDefaultAsync(f => f.Id == productFileId && f.ProductId == id, ct);
            if (link is null) return Results.NotFound();
            // Only the link is removed: the stored file and its UploadedFiles row are kept,
            // exactly like the desktop gallery.
            link.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- attributes ------------------------------------------------------------------
        group.MapPut("/{id:int}/attributes", async (int id, List<AttribValue> body, SadrDbContext db, CancellationToken ct) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id && !p.Deleted, ct)) return Results.NotFound();

            var existing = await db.ProductAttribs.Where(a => a.ProductId == id).ToListAsync(ct);
            foreach (var e in existing) e.Deleted = true;

            foreach (var item in body.Where(x => x.CategoryAttributeId > 0))
            {
                var categoryAttrib = await db.CategoryAttributes.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == item.CategoryAttributeId, ct);
                var row = existing.FirstOrDefault(a => a.CategoryAttribId == item.CategoryAttributeId)
                          ?? new ProductAttrib { ProductId = id, RecordUniqueId = Guid.NewGuid() };
                if (row.Id == 0) db.ProductAttribs.Add(row);
                row.CategoryAttribId = item.CategoryAttributeId;
                row.AttribId = categoryAttrib?.AttributeId ?? item.AttributeId;
                row.Value = item.Value ?? "";
                row.Deleted = false;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- extra categories ------------------------------------------------------------
        group.MapPut("/{id:int}/extra-categories", async (int id, List<int> categoryIds, SadrDbContext db, CancellationToken ct) =>
        {
            if (!await db.Products.AnyAsync(p => p.Id == id && !p.Deleted, ct)) return Results.NotFound();
            var existing = await db.ProductExtraCategories.Where(e => e.ProductId == id).ToListAsync(ct);
            foreach (var e in existing) e.Deleted = true;

            foreach (var categoryId in categoryIds.Distinct().Where(c => c > 0))
            {
                var row = existing.FirstOrDefault(e => e.CategoryId == categoryId)
                          ?? new ProductExtraCategory { ProductId = id, CategoryId = categoryId, RecordUniqueId = Guid.NewGuid() };
                if (row.Id == 0) db.ProductExtraCategories.Add(row);
                row.Deleted = false;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- stored files ----------------------------------------------------------------
        // Mapped on the root so the URL is /api/files/{id} and works for every entity that
        // stores a file, not just products.
        app.MapGet("/api/files/{fileId:int}", async (int fileId, SadrDbContext db, FileStore store, CancellationToken ct) =>
        {
            var file = await db.UploadedFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
            if (file is null) return Results.NotFound();
            var path = store.PhysicalPath(file.FileName);
            if (path is null) return Results.NotFound();
            var contentType = file.Extension switch
            {
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "image/jpeg"
            };
            return Results.File(path, contentType, enableRangeProcessing: true);
        }).WithTags("files");
    }

    /// <summary>
    /// New product rule from the desktop app: stock always lives in a warehouse, so a product
    /// can only be created once a warehouse exists — the default one is created automatically.
    /// </summary>
    private static async Task<IResult?> SaveNewAsync(SadrDbContext db, Product body, CancellationToken ct)
    {
        body.Id = 0;
        body.Name = (body.Name ?? "").Trim();
        if (body.Name.Length == 0) return Results.BadRequest(new { message = "نام کالا الزامی است." });
        if (body.MainCategoryId <= 0)
            return Results.BadRequest(new { message = "دسته‌بندی کالا الزامی است." });
        if (body.MainUnitId <= 0)
            return Results.BadRequest(new { message = "واحد کالا الزامی است." });

        body.Code = CodeRules.NormalizeDigits(body.Code ?? "");
        if (body.Code.Length == 0) body.Code = $"P-{DateTime.Now:yyMMddHHmmss}";
        if (await db.Products.AnyAsync(p => !p.Deleted && p.Code == body.Code, ct))
            return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });

        await DefaultWarehouse.EnsureAsync(db);
        db.Products.Add(body);
        await db.SaveChangesAsync(ct);
        return null;
    }
}
