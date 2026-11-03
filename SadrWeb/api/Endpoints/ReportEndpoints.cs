using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

/// <summary>Dashboards and reports — the data behind the desktop start pages.</summary>
public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("reports");

        // --- balances: cash boxes + bank accounts (موجودی صندوق‌ها و بانک) ---------------
        group.MapGet("/balances", async (SadrDbContext db, CancellationToken ct) =>
        {
            var cashes = await db.Cashes.AsNoTracking().Where(c => !c.Deleted)
                .Select(c => new { c.Id, c.Name, c.Code, balance = c.CurrentBalance })
                .ToListAsync(ct);
            var banks = await db.BankAccounts.AsNoTracking().Where(a => !a.Deleted)
                .Select(a => new
                {
                    a.Id, a.Name, a.AccountNumber, balance = a.CurrentBalance,
                    bankName = db.Banks.Where(b => b.Id == db.BankBranches
                        .Where(br => br.Id == a.BankBranchId).Select(br => br.BankId).FirstOrDefault())
                        .Select(b => b.Name).FirstOrDefault()
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                cashes,
                bankAccounts = banks,
                totalCash = cashes.Sum(c => c.balance),
                totalBank = banks.Sum(b => b.balance),
                total = cashes.Sum(c => c.balance) + banks.Sum(b => b.balance)
            });
        });

        // --- cheque due dashboard (سررسید چک‌ها) -----------------------------------------
        group.MapGet("/cheques/due", async (SadrDbContext db, int days = 30, CancellationToken ct = default) =>
        {
            var today = DateTime.Today;
            var limit = today.AddDays(Math.Clamp(days, 1, 365));

            var rows = await db.Cheques.AsNoTracking()
                .Where(c => !c.Deleted && c.Status != ChequeConsts.StatusCanceled)
                .OrderBy(c => c.DateG).ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id, c.Number, c.Date, c.DateG, c.Amount, c.Direction, c.Status,
                    c.RecieverFullName, c.BankAccountId,
                    AccountName = db.BankAccounts.Where(a => a.Id == c.BankAccountId).Select(a => a.Name).FirstOrDefault(),
                    overdue = c.DateG < today,
                    dueSoon = c.DateG >= today && c.DateG <= limit
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                overdue = rows.Where(r => r.overdue && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank),
                dueSoon = rows.Where(r => r.dueSoon && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank),
                bounced = rows.Where(r => r.Status == ChequeConsts.StatusBounced),
                totals = new
                {
                    received = rows.Where(r => r.Direction == ChequeConsts.DirectionReceived
                                               && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank)
                        .Sum(r => r.Amount),
                    issued = rows.Where(r => r.Direction == ChequeConsts.DirectionIssued
                                             && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank)
                        .Sum(r => r.Amount)
                }
            });
        });

        // --- invoice summary --------------------------------------------------------------
        group.MapGet("/invoices/summary", async (SadrDbContext db, DateTime? from, DateTime? to,
            CancellationToken ct = default) =>
        {
            var start = (from ?? DateTime.Today.AddMonths(-1)).Date;
            var end = (to ?? DateTime.Today).Date;

            var invoices = await db.Invoices.AsNoTracking()
                .Where(i => !i.Deleted && i.InvoiceDateG >= start && i.InvoiceDateG < end.AddDays(1))
                .Select(i => new { i.Id, i.InvoiceType, i.Status, i.TotalPrice, i.TotalDiscount })
                .ToListAsync(ct);

            var paid = await db.AccountTransactions.AsNoTracking()
                .Where(t => !t.Deleted && t.InvoiceId != null && t.DateG >= start && t.DateG < end.AddDays(1))
                .Select(t => new { t.InvoiceId, t.Value })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                from = start,
                to = end,
                byType = new[]
                {
                    new
                    {
                        type = InvoiceTypeConsts.Sell, label = "فروش",
                        count = invoices.Count(i => i.InvoiceType == InvoiceTypeConsts.Sell),
                        total = invoices.Where(i => i.InvoiceType == InvoiceTypeConsts.Sell).Sum(i => i.TotalPrice)
                    },
                    new
                    {
                        type = InvoiceTypeConsts.Buy, label = "خرید",
                        count = invoices.Count(i => i.InvoiceType == InvoiceTypeConsts.Buy),
                        total = invoices.Where(i => i.InvoiceType == InvoiceTypeConsts.Buy).Sum(i => i.TotalPrice)
                    },
                    new
                    {
                        type = InvoiceTypeConsts.PreInvoice, label = "پیش‌فاکتور",
                        count = invoices.Count(i => i.InvoiceType == InvoiceTypeConsts.PreInvoice),
                        total = invoices.Where(i => i.InvoiceType == InvoiceTypeConsts.PreInvoice).Sum(i => i.TotalPrice)
                    }
                },
                byStatus = InvoiceStatusConsts.Draft == 0
                    ? new[]
                    {
                        new { status = 0, label = "پیش‌نویس", count = invoices.Count(i => i.Status == 0) },
                        new { status = 1, label = "صادر شده", count = invoices.Count(i => i.Status == 1) },
                        new { status = 2, label = "تأیید شده", count = invoices.Count(i => i.Status == 2) },
                        new { status = 3, label = "لغو شده", count = invoices.Count(i => i.Status == 3) }
                    }
                    : Array.Empty<object>(),
                total = invoices.Sum(i => i.TotalPrice),
                discount = invoices.Sum(i => i.TotalDiscount),
                paid = paid.Sum(p => p.Value)
            });
        });

        // --- one-call dashboard for the SPA home page -------------------------------------
        group.MapGet("/dashboard", async (SadrDbContext db, CancellationToken ct) =>
        {
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var cashTotal = await db.Cashes.Where(c => !c.Deleted).SumAsync(c => (decimal?)c.CurrentBalance, ct) ?? 0m;
            var bankTotal = await db.BankAccounts.Where(a => !a.Deleted).SumAsync(a => (decimal?)a.CurrentBalance, ct) ?? 0m;
            var sales = await db.Invoices.Where(i => !i.Deleted && i.InvoiceType == InvoiceTypeConsts.Sell
                                                     && i.InvoiceDateG >= monthStart)
                .SumAsync(i => (decimal?)i.TotalPrice, ct) ?? 0m;
            var purchases = await db.Invoices.Where(i => !i.Deleted && i.InvoiceType == InvoiceTypeConsts.Buy
                                                         && i.InvoiceDateG >= monthStart)
                .SumAsync(i => (decimal?)i.TotalPrice, ct) ?? 0m;

            return Results.Ok(new
            {
                cash = cashTotal,
                bank = bankTotal,
                totalMoney = cashTotal + bankTotal,
                monthSales = sales,
                monthPurchases = purchases,
                pendingTransfers = await db.WarehouseTransfers.CountAsync(t => !t.Deleted && !t.Accepted, ct),
                products = await db.Products.CountAsync(p => !p.Deleted, ct),
                invoices = await db.Invoices.CountAsync(i => !i.Deleted, ct),
                people = await db.People.CountAsync(p => !p.Deleted, ct),
                chequesInHand = await db.Cheques.CountAsync(c => !c.Deleted
                    && c.Status == ChequeConsts.StatusInHand, ct),
                projects = await db.Projects.CountAsync(p => !p.Deleted, ct),
                lowStock = await LowStockCountAsync(db, ct)
            });
        });
    }

    private static async Task<int> LowStockCountAsync(SadrDbContext db, CancellationToken ct)
    {
        // A product counts as low stock when its accepted stock is at or below zero.
        var moves = await db.WarehouseTransfers.AsNoTracking().Where(t => !t.Deleted && t.Accepted)
            .GroupBy(t => t.ProductId)
            .Select(g => new
            {
                ProductId = g.Key,
                Net = g.Sum(t => t.Direction == WarehouseTransfer.DirectionIn ? t.Quantity : -t.Quantity)
            })
            .ToDictionaryAsync(x => x.ProductId, x => x.Net, ct);

        var products = await db.Products.AsNoTracking().Where(p => !p.Deleted)
            .Select(p => new { p.Id, p.InitialQuantity }).ToListAsync(ct);

        return products.Count(p => p.InitialQuantity + moves.GetValueOrDefault(p.Id) <= 0);
    }
}
