using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record AccountTransactionInput(int? BankAccountId, int? CashId, int Type, int TransActionSystem,
    decimal Value, DateTime? DateG, string? Description, int? InvoiceId, int? PersonId, int? CompanyId);

public static class MoneyEndpoints
{
    public static void MapMoneyEndpoints(this IEndpointRouteBuilder app)
    {
        MapCashes(app);
        MapTransactions(app);
    }

    // ---------------------------------------------------------------- cash boxes
    private static void MapCashes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cashes").WithTags("cashes");

        group.MapGet("/", async (SadrDbContext db, CancellationToken ct) =>
        {
            var rows = await db.Cashes.AsNoTracking().Where(c => !c.Deleted).OrderBy(c => c.Name)
                .Select(c => new
                {
                    c.Id, c.Name, c.Code, c.Description, c.StartBalance, c.CurrentBalance, c.Status,
                    c.ResponcePersonId, c.OwnerPersonId, c.OwnerCompanyId, c.SignPeople,
                    OwnerName = db.People.Where(p => p.Id == c.ResponcePersonId)
                        .Select(p => p.FirstName + " " + p.LastName).FirstOrDefault()
                })
                .ToListAsync(ct);
            return Results.Ok(new { items = rows, totalBalance = rows.Sum(r => r.CurrentBalance) });
        });

        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
            await db.Cashes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is { } c
                ? Results.Ok(c) : Results.NotFound());

        group.MapPost("/", async (Cash body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            body.Name = (body.Name ?? "").Trim();
            if (body.Name.Length == 0) return Results.BadRequest(new { message = "نام صندوق الزامی است." });
            if (body.ResponcePersonId <= 0) return Results.BadRequest(new { message = "مسئول صندوق الزامی است." });
            if (!string.IsNullOrWhiteSpace(body.Code))
            {
                body.Code = CodeRules.NormalizeDigits(body.Code);
                if (await db.Cashes.AnyAsync(c => !c.Deleted && c.Code == body.Code, ct))
                    return Results.Conflict(new { message = CodeRules.MsgCodeDuplicate });
            }
            body.CurrentBalance = body.StartBalance;
            db.Cashes.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cashes/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, Cash body, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Cashes.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (existing is null) return Results.NotFound();

            var current = existing.CurrentBalance;
            body.Id = id;
            body.CreateDateTime = existing.CreateDateTime;
            body.RecordUniqueId = existing.RecordUniqueId;
            body.CurrentBalance = current;
            db.Entry(existing).CurrentValues.SetValues(body);
            await db.SaveChangesAsync(ct);
            await CashLedger.RecalculateAsync(db, null, id);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var cash = await db.Cashes.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cash is null) return Results.NotFound();
            cash.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
    }

    // ---------------------------------------------------------------- money movements
    private static void MapTransactions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/account-transactions").WithTags("money");

        group.MapGet("/", async (SadrDbContext db, int? type, int? system, int? bankAccountId, int? cashId,
            DateTime? from, DateTime? to, int page = 1, int pageSize = 200, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var query = db.AccountTransactions.AsNoTracking().Where(t => !t.Deleted);
            if (type is int ty) query = query.Where(t => t.Type == ty);
            if (system is int sy) query = query.Where(t => t.TransActionSystem == sy);
            if (bankAccountId is int ba) query = query.Where(t => t.BankAccountId == ba);
            if (cashId is int ca) query = query.Where(t => t.CashId == ca);
            if (from is DateTime f) query = query.Where(t => t.DateG >= f.Date);
            if (to is DateTime tt) query = query.Where(t => t.DateG < tt.Date.AddDays(1));

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => new
                {
                    t.Id, t.Type, t.TransActionSystem, t.Value, t.Date, t.DateG, t.Description,
                    t.BankAccountId, t.CashId, t.InvoiceId, t.PersonId, t.CompnayId,
                    BankAccountName = db.BankAccounts.Where(a => a.Id == t.BankAccountId).Select(a => a.Name).FirstOrDefault(),
                    CashName = db.Cashes.Where(c => c.Id == t.CashId).Select(c => c.Name).FirstOrDefault(),
                    InvoiceNumber = db.Invoices.Where(i => i.Id == t.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault(),
                    PersonName = db.People.Where(p => p.Id == t.PersonId)
                        .Select(p => p.FirstName + " " + p.LastName).FirstOrDefault()
                })
                .ToListAsync(ct);

            var received = rows.Where(r => r.Type == AccountTransactionTypeConsts.Receipt).Sum(r => r.Value);
            var paid = rows.Where(r => r.Type == AccountTransactionTypeConsts.Payment).Sum(r => r.Value);

            return Results.Ok(new { items = rows, total, page, pageSize, received, paid, net = received - paid });
        });

        group.MapPost("/", async (AccountTransactionInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (body.Value <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
            if (body.Type is not (AccountTransactionTypeConsts.Receipt or AccountTransactionTypeConsts.Payment
                or AccountTransactionTypeConsts.Transfer))
                return Results.BadRequest(new { message = "نوع تراکنش نامعتبر است." });

            var isCash = body.TransActionSystem == TransactionSystemConsts.Cash;
            if (isCash && body.CashId is not > 0) return Results.BadRequest(new { message = "صندوق را انتخاب کنید." });
            if (!isCash && body.BankAccountId is not > 0) return Results.BadRequest(new { message = "حساب بانکی را انتخاب کنید." });
            if (isCash && !await db.Cashes.AnyAsync(c => c.Id == body.CashId && !c.Deleted, ct))
                return Results.BadRequest(new { message = "صندوق یافت نشد." });
            if (!isCash && !await db.BankAccounts.AnyAsync(a => a.Id == body.BankAccountId && !a.Deleted, ct))
                return Results.BadRequest(new { message = "حساب بانکی یافت نشد." });

            var when = (body.DateG ?? DateTime.Today).Date;
            var transaction = new AccountTransaction
            {
                DateG = when,
                Date = PersianDate.ToPersian(when),
                Value = body.Value,
                Type = body.Type,
                TransActionSystem = body.TransActionSystem,
                BankAccountId = isCash ? null : body.BankAccountId,
                CashId = isCash ? body.CashId : null,
                Description = body.Description ?? "",
                InvoiceId = body.InvoiceId is > 0 ? body.InvoiceId : null,
                PersonId = body.PersonId is > 0 ? body.PersonId : null,
                CompnayId = body.CompanyId is > 0 ? body.CompanyId : null,
                RecordUniqueId = Guid.NewGuid()
            };
            await CashLedger.PostAsync(db, transaction);

            if (transaction.InvoiceId is int invoiceId)
            {
                db.InvoiceMoneyTransactions.Add(new InvoiceMoneyTransaction
                {
                    InvoiceId = invoiceId,
                    AccountTransactionId = transaction.Id,
                    RecordUniqueId = Guid.NewGuid()
                });
                await db.SaveChangesAsync(ct);
            }

            return Results.Created($"/api/account-transactions/{transaction.Id}", transaction);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, AccountTransactionInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var transaction = await db.AccountTransactions.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (transaction is null) return Results.NotFound();
            if (body.Value <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });

            var previousBank = transaction.BankAccountId;
            var previousCash = transaction.CashId;
            var isCash = body.TransActionSystem == TransactionSystemConsts.Cash;

            transaction.Type = body.Type;
            transaction.TransActionSystem = body.TransActionSystem;
            transaction.Value = body.Value;
            transaction.DateG = (body.DateG ?? transaction.DateG).Date;
            transaction.Date = PersianDate.ToPersian(transaction.DateG);
            transaction.Description = body.Description ?? transaction.Description;
            transaction.BankAccountId = isCash ? null : body.BankAccountId;
            transaction.CashId = isCash ? body.CashId : null;
            transaction.InvoiceId = body.InvoiceId is > 0 ? body.InvoiceId : null;
            transaction.PersonId = body.PersonId is > 0 ? body.PersonId : null;
            transaction.CompnayId = body.CompanyId is > 0 ? body.CompanyId : null;

            await db.SaveChangesAsync(ct);
            await CashLedger.RecalculateAsync(db, previousBank, previousCash);
            await CashLedger.RecalculateAsync(db, transaction.BankAccountId, transaction.CashId);
            return Results.Ok(transaction);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var transaction = await db.AccountTransactions.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (transaction is null) return Results.NotFound();
            await CashLedger.RemoveAsync(db, transaction);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        // --- manual bank <-> cash transfer ----------------------------------------------
        group.MapPost("/transfer", async (int bankAccountId, int cashId, decimal value, DateTime? dateG,
            string? description, SadrDbContext db, CancellationToken ct) =>
        {
            if (value <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
            if (!await db.Cashes.AnyAsync(c => c.Id == cashId && !c.Deleted, ct))
                return Results.BadRequest(new { message = "صندوق یافت نشد." });
            if (!await db.BankAccounts.AnyAsync(a => a.Id == bankAccountId && !a.Deleted, ct))
                return Results.BadRequest(new { message = "حساب بانکی یافت نشد." });

            var when = (dateG ?? DateTime.Today).Date;
            var depart = new AccountTransaction
            {
                BankAccountId = bankAccountId,
                Type = AccountTransactionTypeConsts.Payment,
                TransActionSystem = TransactionSystemConsts.Bank,
                Value = value,
                DateG = when,
                Date = PersianDate.ToPersian(when),
                Description = description ?? "انتقال به صندوق",
                RecordUniqueId = Guid.NewGuid()
            };
            await CashLedger.PostAsync(db, depart);

            var arrive = new AccountTransaction
            {
                CashId = cashId,
                Type = AccountTransactionTypeConsts.Transfer,
                TransActionSystem = TransactionSystemConsts.Cash,
                Value = value,
                DateG = when,
                Date = PersianDate.ToPersian(when),
                Description = description ?? "انتقال از بانک",
                RecordUniqueId = Guid.NewGuid()
            };
            await CashLedger.PostAsync(db, arrive);

            return Results.Ok(new { depart = depart.Id, arrive = arrive.Id });
        }).RequireAuthorization(Policies.AnyUser);
    }
}
