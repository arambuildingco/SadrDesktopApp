using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

/// <summary>One invoice line as the clients send it (the same fields the desktop grid edits).</summary>
public record InvoiceLineInput(
    int? Id,           // existing InvoiceDetails.Id when editing, null for a new line
    int ProductId,
    int UnitId,
    int? PriceId,
    decimal? UnitPrice,
    int? CurrencyId,
    decimal Amount,
    double? ExtraPercent,
    double? DiscountPercent,
    decimal? DiscountAmount);

public record InvoiceInput(
    int InvoiceType,
    int Status,
    string? InvoiceNumber,
    string? Code,
    string Description,
    DateTime? InvoiceDateG,
    int? PartyId,      // customer for sell/pre-invoice, provider for buy
    List<InvoiceLineInput> Lines);

public record InvoiceMoneyInput(int Type, int TransActionSystem, int? BankAccountId, int? CashId,
    decimal Value, DateTime? DateG, string? Description);

public static class InvoiceEndpoints
{
    /// <summary>Line total — the desktop formula, rounded to whole rials like the grid.</summary>
    public static decimal LineTotal(InvoiceLineInput l) =>
        Math.Round((l.UnitPrice ?? 0m) * l.Amount
                   * (1 + (decimal)((l.ExtraPercent ?? 0) / 100.0))
                   * (1 - (decimal)((l.DiscountPercent ?? 0) / 100.0))
                   - (l.DiscountAmount ?? 0m), 0);

    public static decimal LineDiscount(InvoiceLineInput l) =>
        Math.Round(((l.UnitPrice ?? 0m) * l.Amount * (decimal)((l.DiscountPercent ?? 0) / 100.0))
                   + (l.DiscountAmount ?? 0m), 0);

    public static void MapInvoiceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/invoices").WithTags("invoices");

        // --- list -----------------------------------------------------------------------
        group.MapGet("/", async (SadrDbContext db, int? type, int? status, int? partyId,
            DateTime? from, DateTime? to, string? q, int page = 1, int pageSize = 50,
            CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = db.Invoices.AsNoTracking().Where(i => !i.Deleted);
            if (type is int t) query = query.Where(i => i.InvoiceType == t);
            if (status is int s) query = query.Where(i => i.Status == s);
            if (partyId is int p) query = query.Where(i => i.CustomerId == p || i.ProviderId == p);
            if (from is DateTime f) query = query.Where(i => i.InvoiceDateG >= f.Date);
            if (to is DateTime tt) query = query.Where(i => i.InvoiceDateG < tt.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(i => i.InvoiceNumber.Contains(term) || i.Description.Contains(term));
            }

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(i => i.InvoiceDateG).ThenByDescending(i => i.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(i => new
                {
                    i.Id, i.InvoiceNumber, i.InvoiceDate, i.InvoiceDateG, i.InvoiceType, i.Status,
                    i.TotalPrice, i.TotalDiscount, i.CustomerId, i.ProviderId, i.Description,
                    PartyName = db.CustomersProviders
                        .Where(c => c.Id == (i.CustomerId ?? i.ProviderId)).Select(c => c.Name).FirstOrDefault(),
                    PaidAmount = db.AccountTransactions
                        .Where(t => !t.Deleted && t.InvoiceId == i.Id).Sum(t => (decimal?)t.Value) ?? 0m
                })
                .ToListAsync(ct);

            return Results.Ok(new { items = rows, total, page, pageSize });
        });

        // --- one invoice with lines, money and warehouse links ---------------------------
        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.AsNoTracking().Include(i => i.Details)
                .FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return Results.NotFound();

            var lines = invoice.Details.Where(d => !d.Deleted).OrderBy(d => d.Id).Select(d => new
            {
                d.Id, d.ProductId, d.UnitId, d.PriceId, d.UnitPriceAmount, d.CurrencyId, d.Amount,
                d.ExtraPersentage, d.DiscountPersentage, d.TotalDiscount, d.TotalPriceAmount,
                ProductName = db.Products.Where(p => p.Id == d.ProductId).Select(p => p.Name).FirstOrDefault()
            }).ToList();

            var money = await db.AccountTransactions.AsNoTracking()
                .Where(t => !t.Deleted && t.InvoiceId == id)
                .Select(t => new
                {
                    t.Id, t.Type, t.TransActionSystem, t.Value, t.Date, t.DateG, t.Description,
                    t.BankAccountId, t.CashId
                })
                .ToListAsync(ct);

            var transfers = await db.WarehouseTransfers.AsNoTracking()
                .Where(t => !t.Deleted && t.InvoiceId == id)
                .Select(t => new { t.Id, t.ProductId, t.Direction, t.Quantity, t.Accepted, t.TransferDate })
                .ToListAsync(ct);

            return Results.Ok(new { invoice, lines, money, transfers });
        });

        // --- create ---------------------------------------------------------------------
        group.MapPost("/", async (InvoiceInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var problem = Validate(body);
            if (problem is not null) return Results.BadRequest(new { message = problem });
            if (body.Lines.Count == 0) return Results.BadRequest(new { message = "حداقل یک ردیف کالا لازم است." });

            var productProblem = await CheckProductsAsync(db, body.Lines, ct);
            if (productProblem is not null) return Results.BadRequest(new { message = productProblem });

            var invoice = new Invoice { RecordUniqueId = Guid.NewGuid() };
            db.Invoices.Add(invoice);
            ApplyHeader(invoice, body);

            invoice.InvoiceDateG = (body.InvoiceDateG ?? DateTime.Today).Date;
            invoice.InvoiceDate = PersianDate.ToPersian(invoice.InvoiceDateG);
            invoice.InvoiceNumber = string.IsNullOrWhiteSpace(body.InvoiceNumber)
                ? await NextNumberAsync(db, body.InvoiceType, ct)
                : body.InvoiceNumber!.Trim();

            foreach (var line in body.Lines)
                db.InvoiceDetails.Add(NewDetail(invoice, line));

            invoice.TotalPrice = body.Lines.Sum(LineTotal);
            invoice.TotalDiscount = body.Lines.Sum(LineDiscount);

            await db.SaveChangesAsync(ct);

            // Buy/sell invoices queue stock movements; a warehouse keeper accepts them later.
            if (invoice.InvoiceType is InvoiceTypeConsts.Buy or InvoiceTypeConsts.Sell)
                await WarehouseStock.SyncInvoiceTransfersAsync(db, invoice, null);

            return Results.Created($"/api/invoices/{invoice.Id}", new
            {
                invoice.Id, invoice.InvoiceNumber, invoice.TotalPrice, invoice.TotalDiscount,
                warnings = await StockWarningsAsync(db, invoice, body.Lines, ct)
            });
        }).RequireAuthorization(Policies.Invoices);

        // --- update ---------------------------------------------------------------------
        group.MapPut("/{id:int}", async (int id, InvoiceInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var problem = Validate(body);
            if (problem is not null) return Results.BadRequest(new { message = problem });

            var invoice = await db.Invoices.Include(i => i.Details).FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, ct);
            if (invoice is null) return Results.NotFound();

            var productProblem = await CheckProductsAsync(db, body.Lines, ct);
            if (productProblem is not null) return Results.BadRequest(new { message = productProblem });

            ApplyHeader(invoice, body);
            invoice.InvoiceDateG = (body.InvoiceDateG ?? invoice.InvoiceDateG).Date;
            invoice.InvoiceDate = PersianDate.ToPersian(invoice.InvoiceDateG);
            if (!string.IsNullOrWhiteSpace(body.InvoiceNumber)) invoice.InvoiceNumber = body.InvoiceNumber!.Trim();

            // Lines are matched by identity (not product), so one product may appear twice.
            var keptIds = body.Lines.Where(l => l.Id is > 0).Select(l => l.Id!.Value).ToHashSet();
            foreach (var detail in invoice.Details.Where(d => !d.Deleted && !keptIds.Contains(d.Id)))
                detail.Deleted = true;

            foreach (var line in body.Lines)
            {
                var existing = line.Id is > 0 ? invoice.Details.FirstOrDefault(d => d.Id == line.Id) : null;
                if (existing is null)
                {
                    db.InvoiceDetails.Add(NewDetail(invoice, line));
                }
                else
                {
                    existing.ProductId = line.ProductId;
                    existing.UnitId = line.UnitId > 0 ? line.UnitId : existing.UnitId;
                    existing.PriceId = line.PriceId;
                    existing.UnitPriceAmount = line.UnitPrice;
                    existing.CurrencyId = line.CurrencyId;
                    existing.Amount = line.Amount;
                    existing.ExtraPersentage = line.ExtraPercent;
                    existing.DiscountPersentage = line.DiscountPercent;
                    existing.TotalDiscount = LineDiscount(line);
                    existing.TotalPriceAmount = LineTotal(line);
                    existing.Deleted = false;
                }
            }

            invoice.TotalPrice = body.Lines.Sum(LineTotal);
            invoice.TotalDiscount = body.Lines.Sum(LineDiscount);
            await db.SaveChangesAsync(ct);

            if (invoice.InvoiceType is InvoiceTypeConsts.Buy or InvoiceTypeConsts.Sell)
                await WarehouseStock.SyncInvoiceTransfersAsync(db, invoice, null);

            return Results.Ok(new { invoice.Id, invoice.InvoiceNumber, invoice.TotalPrice, invoice.TotalDiscount });
        }).RequireAuthorization(Policies.Invoices);

        // --- delete ---------------------------------------------------------------------
        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.Include(i => i.Details).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return Results.NotFound();

            invoice.Deleted = true;
            foreach (var d in invoice.Details) d.Deleted = true;

            // Pending transfers disappear with the invoice; accepted ones stay, because the
            // goods physically moved and the warehouse already confirmed it.
            var transfers = await db.WarehouseTransfers.Where(t => t.InvoiceId == id && !t.Deleted).ToListAsync(ct);
            foreach (var t in transfers.Where(t => !t.Accepted)) t.Deleted = true;

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Invoices);

        // --- status ---------------------------------------------------------------------
        group.MapPost("/{id:int}/status", async (int id, int status, SadrDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, ct);
            if (invoice is null) return Results.NotFound();
            if (status is < InvoiceStatusConsts.Draft or > InvoiceStatusConsts.Cancelled)
                return Results.BadRequest(new { message = "وضعیت نامعتبر است." });
            invoice.Status = status;
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { invoice.Id, invoice.Status });
        }).RequireAuthorization(Policies.Invoices);

        // --- money: a payment/receipt against the invoice --------------------------------
        group.MapPost("/{id:int}/money", async (int id, InvoiceMoneyInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == id && !i.Deleted, ct);
            if (invoice is null) return Results.NotFound();
            if (body.Value <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });

            var when = (body.DateG ?? DateTime.Today).Date;
            var isCash = body.TransActionSystem == TransactionSystemConsts.Cash;
            if (isCash && body.CashId is not > 0)
                return Results.BadRequest(new { message = "صندوق را انتخاب کنید." });
            if (!isCash && body.BankAccountId is not > 0)
                return Results.BadRequest(new { message = "حساب بانکی را انتخاب کنید." });

            var transaction = new AccountTransaction
            {
                DateG = when,
                Date = PersianDate.ToPersian(when),
                Value = body.Value,
                Description = body.Description ?? $"فاکتور {invoice.InvoiceNumber}",
                Type = body.Type,
                TransActionSystem = body.TransActionSystem,
                InvoiceId = id,
                BankAccountId = isCash ? null : body.BankAccountId,
                CashId = isCash ? body.CashId : null,
                PersonId = null,
                RecordUniqueId = Guid.NewGuid()
            };
            await CashLedger.PostAsync(db, transaction);

            db.InvoiceMoneyTransactions.Add(new InvoiceMoneyTransaction
            {
                InvoiceId = id,
                AccountTransactionId = transaction.Id,
                RecordUniqueId = Guid.NewGuid()
            });
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/invoices/{id}/money/{transaction.Id}",
                new { transaction.Id, transaction.Value, transaction.Type, transaction.Date });
        }).RequireAuthorization(Policies.Invoices);

        // --- what an invoice still owes ---------------------------------------------------
        group.MapGet("/{id:int}/balance", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var invoice = await db.Invoices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct);
            if (invoice is null) return Results.NotFound();
            var paid = await db.AccountTransactions.AsNoTracking()
                .Where(t => !t.Deleted && t.InvoiceId == id)
                .SumAsync(t => (decimal?)t.Value, ct) ?? 0m;
            return Results.Ok(new { invoice.Id, total = invoice.TotalPrice, paid, remaining = invoice.TotalPrice - paid });
        });
    }

    private static string? Validate(InvoiceInput body)
    {
        if (body.InvoiceType is < InvoiceTypeConsts.Sell or > InvoiceTypeConsts.PreInvoice)
            return "نوع فاکتور نامعتبر است.";
        foreach (var line in body.Lines)
        {
            if (line.ProductId <= 0) return "کالای هر ردیف الزامی است.";
            if (line.Amount <= 0) return "تعداد هر ردیف باید بزرگ‌تر از صفر باشد.";
        }
        return null;
    }

    private static async Task<string?> CheckProductsAsync(SadrDbContext db, List<InvoiceLineInput> lines, CancellationToken ct)
    {
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var alive = await db.Products.Where(p => ids.Contains(p.Id) && !p.Deleted).Select(p => p.Id).ToListAsync(ct);
        var missing = ids.Except(alive).ToList();
        return missing.Count == 0 ? null : $"کالای شماره {string.Join("، ", missing)} یافت نشد.";
    }

    private static void ApplyHeader(Invoice invoice, InvoiceInput body)
    {
        invoice.InvoiceType = body.InvoiceType;
        invoice.Status = body.Status;
        invoice.Code = string.IsNullOrWhiteSpace(body.Code) ? null : body.Code!.Trim();
        invoice.Description = body.Description ?? "";
        invoice.CustomerId = body.InvoiceType == InvoiceTypeConsts.Buy ? null : body.PartyId is > 0 ? body.PartyId : null;
        invoice.ProviderId = body.InvoiceType == InvoiceTypeConsts.Buy ? body.PartyId is > 0 ? body.PartyId : null : null;
    }

    private static InvoiceDetail NewDetail(Invoice invoice, InvoiceLineInput line) => new()
    {
        Invoice = invoice,
        RecordUniqueId = Guid.NewGuid(),
        ProductId = line.ProductId,
        UnitId = line.UnitId,
        PriceId = line.PriceId,
        UnitPriceAmount = line.UnitPrice,
        CurrencyId = line.CurrencyId,
        Amount = line.Amount,
        ExtraPersentage = line.ExtraPercent,
        DiscountPersentage = line.DiscountPercent,
        Status = 0,
        TotalDiscount = LineDiscount(line),
        TotalPriceAmount = LineTotal(line)
    };

    /// <summary>Invoice numbers look like S-1405-0001 (S sell, B buy, P pre-invoice).</summary>
    public static async Task<string> NextNumberAsync(SadrDbContext db, int type, CancellationToken ct)
    {
        var year = PersianDate.Today[..4];
        var prefix = type switch
        {
            InvoiceTypeConsts.Buy => "B-",
            InvoiceTypeConsts.PreInvoice => "P-",
            _ => "S-"
        };
        var count = await db.Invoices.CountAsync(i => i.InvoiceType == type, ct) + 1;
        return $"{prefix}{year}-{count:0000}";
    }

    /// <summary>
    /// Soft warning (never a block) for sell invoices: the projected stock would go negative.
    /// Mirrors the desktop MessageBox that asks the seller to confirm.
    /// </summary>
    private static async Task<List<string>> StockWarningsAsync(SadrDbContext db, Invoice invoice,
        List<InvoiceLineInput> lines, CancellationToken ct)
    {
        if (invoice.InvoiceType != InvoiceTypeConsts.Sell) return new();

        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.InitialQuantity }).ToDictionaryAsync(p => p.Id, ct);

        var others = await db.WarehouseTransfers
            .Where(t => !t.Deleted && productIds.Contains(t.ProductId) && t.InvoiceId != invoice.Id)
            .Select(t => new { t.ProductId, t.Direction, t.Quantity })
            .ToListAsync(ct);
        var net = others.GroupBy(t => t.ProductId).ToDictionary(g => g.Key,
            g => g.Sum(t => t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity));

        var warnings = new List<string>();
        foreach (var group in lines.GroupBy(l => l.ProductId))
        {
            var projected = (products.TryGetValue(group.Key, out var p) ? p.InitialQuantity : 0m)
                            + net.GetValueOrDefault(group.Key)
                            - group.Sum(l => l.Amount);
            if (projected < 0)
                warnings.Add($"{(products.TryGetValue(group.Key, out var p2) ? p2.Name : "کالای #" + group.Key)}: کسری {(-projected):N0}");
        }
        return warnings;
    }
}
