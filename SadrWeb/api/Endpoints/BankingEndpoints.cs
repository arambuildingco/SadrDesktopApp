using Microsoft.EntityFrameworkCore;
using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

public record ChequeInput(int BankAccountId, DateTime? DateG, string? Number, string? SerialNumber,
    string? RecieverFullName, string? RecieverCode, string? Description, decimal Amount,
    int? InvoiceId, int? PersonId, int Direction, int Status);

public record BankTransactionInput(int BankAccountId, int Kind, decimal Amount, DateTime? DateG, string? Description);

public record ChequeStatusInput(int Status);

public static class BankingEndpoints
{
    public static void MapBankingEndpoints(this IEndpointRouteBuilder app)
    {
        MapBankAccounts(app);
        MapBankTransactions(app);
        MapCheques(app);
    }

    // ---------------------------------------------------------------- bank accounts
    private static void MapBankAccounts(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-accounts").WithTags("bank-accounts");

        group.MapGet("/", async (SadrDbContext db, string? q, bool includeDeleted = false, CancellationToken ct = default) =>
        {
            var query = db.BankAccounts.AsNoTracking().AsQueryable();
            if (!includeDeleted) query = query.Where(a => !a.Deleted);
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(a => a.Name.Contains(term) || a.AccountNumber.Contains(term));
            }
            var rows = await query.OrderBy(a => a.Name)
                .Select(a => new
                {
                    a.Id, a.Name, a.AccountNumber, a.CardNumber, a.IdNumber, a.AccountType,
                    a.StartBalance, a.CurrentBalance, a.CurrencyId, a.BankBranchId, a.PersonId, a.CompanyId,
                    a.Description, a.SignPeople, a.Deleted,
                    BankName = db.Banks
                        .Where(b => b.Id == db.BankBranches.Where(br => br.Id == a.BankBranchId).Select(br => br.BankId).FirstOrDefault())
                        .Select(b => b.Name).FirstOrDefault(),
                    BranchName = db.BankBranches.Where(br => br.Id == a.BankBranchId).Select(br => br.Name).FirstOrDefault()
                })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
            await db.BankAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct) is { } a
                ? Results.Ok(a) : Results.NotFound());

        group.MapPost("/", async (BankAccount body, SadrDbContext db, CancellationToken ct) =>
        {
            body.Id = 0;
            body.Name = (body.Name ?? "").Trim();
            if (body.Name.Length == 0) return Results.BadRequest(new { message = "نام حساب الزامی است." });
            if (body.BankBranchId <= 0) return Results.BadRequest(new { message = "شعبه بانک الزامی است." });
            if (body.CurrencyId <= 0) return Results.BadRequest(new { message = "واحد پول الزامی است." });

            body.CurrentBalance = body.StartBalance; // recalculated from the ledger on every posting
            db.BankAccounts.Add(body);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/bank-accounts/{body.Id}", body);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, BankAccount body, SadrDbContext db, CancellationToken ct) =>
        {
            var existing = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (existing is null) return Results.NotFound();

            var current = existing.CurrentBalance;
            body.Id = id;
            body.CreateDateTime = existing.CreateDateTime;
            body.RecordUniqueId = existing.RecordUniqueId;
            body.CurrentBalance = current;
            db.Entry(existing).CurrentValues.SetValues(body);
            await db.SaveChangesAsync(ct);
            await ChequeLedger.RecalculateBalanceAsync(db, id, DateTime.Now);
            return Results.Ok(existing);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var account = await db.BankAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            if (account is null) return Results.NotFound();
            account.Deleted = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
    }

    // ---------------------------------------------------------------- ledger rows
    private static void MapBankTransactions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/bank-transactions").WithTags("bank-transactions");

        group.MapGet("/", async (SadrDbContext db, int? bankAccountId, DateTime? from, DateTime? to,
            int page = 1, int pageSize = 200, CancellationToken ct = default) =>
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            var query = db.BankTransactions.AsNoTracking().Where(t => !t.Deleted);
            if (bankAccountId is int id) query = query.Where(t => t.BankAccountId == id);
            if (from is DateTime f) query = query.Where(t => t.DateG >= f.Date);
            if (to is DateTime tt) query = query.Where(t => t.DateG < tt.Date.AddDays(1));

            var total = await query.CountAsync(ct);
            var rows = await query.OrderByDescending(t => t.DateG).ThenByDescending(t => t.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(t => new
                {
                    t.Id, t.BankAccountId, t.Kind, t.Amount, t.Date, t.DateG, t.Source, t.ChequeId,
                    t.EventKind, t.Describtion,
                    AccountName = db.BankAccounts.Where(a => a.Id == t.BankAccountId).Select(a => a.Name).FirstOrDefault()
                })
                .ToListAsync(ct);

            var balance = bankAccountId is int account
                ? await ChequeLedger.BalanceOfAsync(db, account)
                : (decimal?)null;

            return Results.Ok(new { items = rows, total, page, pageSize, balance });
        });

        group.MapPost("/", async (BankTransactionInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (body.Amount <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });
            if (body.Kind is not (BankTransaction.KindDeposit or BankTransaction.KindWithdrawal))
                return Results.BadRequest(new { message = "نوع تراکنش نامعتبر است." });
            if (!await db.BankAccounts.AnyAsync(a => a.Id == body.BankAccountId && !a.Deleted, ct))
                return Results.BadRequest(new { message = "حساب بانکی یافت نشد." });

            var row = new BankTransaction
            {
                BankAccountId = body.BankAccountId,
                Kind = body.Kind,
                Amount = body.Amount,
                DateG = (body.DateG ?? DateTime.Today).Date,
                Describtion = body.Description ?? "",
                RecordUniqueId = Guid.NewGuid()
            };
            await ChequeLedger.PostManualAsync(db, row, DateTime.Now);
            return Results.Created($"/api/bank-transactions/{row.Id}", new
            {
                row.Id,
                balance = await ChequeLedger.BalanceOfAsync(db, row.BankAccountId)
            });
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, BankTransactionInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var row = await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (row is null) return Results.NotFound();
            if (row.Source == BankTransaction.SourceCheque)
                return Results.BadRequest(new { message = "این تراکنش از یک چک ساخته شده؛ آن را از صفحه چک‌ها تغییر دهید." });
            if (body.Amount <= 0) return Results.BadRequest(new { message = "مبلغ باید بزرگ‌تر از صفر باشد." });

            var previousAccount = row.BankAccountId;
            row.BankAccountId = body.BankAccountId;
            row.Kind = body.Kind;
            row.Amount = body.Amount;
            row.DateG = (body.DateG ?? row.DateG).Date;
            row.Describtion = body.Description ?? row.Describtion;
            await ChequeLedger.PostManualAsync(db, row, DateTime.Now);
            if (previousAccount != row.BankAccountId)
                await ChequeLedger.RecalculateBalanceAsync(db, previousAccount, DateTime.Now);

            return Results.Ok(new { row.Id, balance = await ChequeLedger.BalanceOfAsync(db, row.BankAccountId) });
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var row = await db.BankTransactions.FirstOrDefaultAsync(t => t.Id == id && !t.Deleted, ct);
            if (row is null) return Results.NotFound();
            if (row.Source == BankTransaction.SourceCheque)
                return Results.BadRequest(new { message = "این تراکنش از یک چک ساخته شده؛ وضعیت چک را تغییر دهید." });

            row.Deleted = true;
            await db.SaveChangesAsync(ct);
            await ChequeLedger.RecalculateBalanceAsync(db, row.BankAccountId, DateTime.Now);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);
    }

    // ---------------------------------------------------------------- cheques
    private static void MapCheques(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/cheques").WithTags("cheques");

        group.MapGet("/", async (SadrDbContext db, int? direction, int? status, int? bankAccountId,
            DateTime? from, DateTime? to, string? q, CancellationToken ct = default) =>
        {
            var query = db.Cheques.AsNoTracking().Where(c => !c.Deleted);
            if (direction is int d) query = query.Where(c => c.Direction == d);
            if (status is int s) query = query.Where(c => c.Status == s);
            if (bankAccountId is int a) query = query.Where(c => c.BankAccountId == a);
            if (from is DateTime f) query = query.Where(c => c.DateG >= f.Date);
            if (to is DateTime tt) query = query.Where(c => c.DateG < tt.Date.AddDays(1));
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(c => c.Number.Contains(term) || c.RecieverFullName.Contains(term)
                                         || c.SerialNumber.Contains(term));
            }

            var rows = await query.OrderBy(c => c.DateG).ThenBy(c => c.Id)
                .Select(c => new
                {
                    c.Id, c.Number, c.SerialNumber, c.Date, c.DateG, c.Amount, c.Direction, c.Status,
                    c.BankAccountId, c.RecieverFullName, c.RecieverCode, c.Describtion, c.InvoiceId, c.PersonId,
                    AccountName = db.BankAccounts.Where(a => a.Id == c.BankAccountId).Select(a => a.Name).FirstOrDefault(),
                    InvoiceNumber = db.Invoices.Where(i => i.Id == c.InvoiceId).Select(i => i.InvoiceNumber).FirstOrDefault(),
                    DaysToDue = EF.Functions.DateDiffDay(DateTime.Today, c.DateG)
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items = rows,
                totals = new
                {
                    receivedInHand = rows.Where(r => r.Direction == ChequeConsts.DirectionReceived
                                                     && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank)
                        .Sum(r => r.Amount),
                    issuedInHand = rows.Where(r => r.Direction == ChequeConsts.DirectionIssued
                                                   && r.Status is ChequeConsts.StatusInHand or ChequeConsts.StatusPassedToBank)
                        .Sum(r => r.Amount)
                }
            });
        });

        group.MapGet("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
            await db.Cheques.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) is { } c
                ? Results.Ok(c) : Results.NotFound());

        group.MapPost("/", async (ChequeInput body, SadrDbContext db, CancellationToken ct) =>
        {
            if (body.Amount <= 0) return Results.BadRequest(new { message = "مبلغ چک باید بزرگ‌تر از صفر باشد." });
            if (body.Direction is not (ChequeConsts.DirectionReceived or ChequeConsts.DirectionIssued))
                return Results.BadRequest(new { message = "جهت چک نامعتبر است." });
            if (!await db.BankAccounts.AnyAsync(a => a.Id == body.BankAccountId && !a.Deleted, ct))
                return Results.BadRequest(new { message = "حساب بانکی یافت نشد." });

            var when = (body.DateG ?? DateTime.Today).Date;
            var cheque = new Cheque
            {
                BankAccountId = body.BankAccountId,
                DateG = when,
                Date = PersianDate.ToPersian(when),
                Number = body.Number ?? "",
                SerialNumber = body.SerialNumber ?? "",
                RecieverFullName = body.RecieverFullName ?? "",
                RecieverCode = body.RecieverCode ?? "",
                Describtion = body.Description ?? "",
                Amount = body.Amount,
                InvoiceId = body.InvoiceId is > 0 ? body.InvoiceId : null,
                PersonId = body.PersonId is > 0 ? body.PersonId : null,
                Direction = body.Direction,
                Status = body.Status,
                RecordUniqueId = Guid.NewGuid()
            };
            db.Cheques.Add(cheque);
            await db.SaveChangesAsync(ct);

            // A cheque saved directly as cashed/spent must hit the bank ledger right away.
            if (cheque.Status == ChequeConsts.StatusCashed)
                await ChequeLedger.SyncChequeAsync(db, cheque, ChequeLedger.EventCashed, when);
            else if (cheque.Status == ChequeConsts.StatusSpent)
                await ChequeLedger.SyncChequeAsync(db, cheque, ChequeLedger.EventSpent, when);

            return Results.Created($"/api/cheques/{cheque.Id}", new
            {
                cheque.Id,
                balance = await ChequeLedger.BalanceOfAsync(db, cheque.BankAccountId)
            });
        }).RequireAuthorization(Policies.AnyUser);

        group.MapPut("/{id:int}", async (int id, ChequeInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var cheque = await db.Cheques.FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
            if (cheque is null) return Results.NotFound();
            if (body.Amount <= 0) return Results.BadRequest(new { message = "مبلغ چک باید بزرگ‌تر از صفر باشد." });

            var previousAccount = cheque.BankAccountId;
            cheque.BankAccountId = body.BankAccountId;
            cheque.DateG = (body.DateG ?? cheque.DateG).Date;
            cheque.Date = PersianDate.ToPersian(cheque.DateG);
            cheque.Number = body.Number ?? cheque.Number;
            cheque.SerialNumber = body.SerialNumber ?? cheque.SerialNumber;
            cheque.RecieverFullName = body.RecieverFullName ?? cheque.RecieverFullName;
            cheque.RecieverCode = body.RecieverCode ?? cheque.RecieverCode;
            cheque.Describtion = body.Description ?? cheque.Describtion;
            cheque.Amount = body.Amount;
            cheque.InvoiceId = body.InvoiceId is > 0 ? body.InvoiceId : null;
            cheque.PersonId = body.PersonId is > 0 ? body.PersonId : null;
            cheque.Direction = body.Direction;
            await db.SaveChangesAsync(ct);

            // Keep the ledger consistent with the (possibly new) account and amount.
            if (ChequeLedger.IsPosted(cheque))
            {
                var eventKind = cheque.Status == ChequeConsts.StatusSpent
                    ? ChequeLedger.EventSpent : ChequeLedger.EventCashed;
                await db.BankTransactions.Where(t => t.ChequeId == cheque.Id && !t.Deleted)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Deleted, true), ct);
                await db.SaveChangesAsync(ct);
                await ChequeLedger.SyncChequeAsync(db, cheque, eventKind, DateTime.Now);
            }
            if (previousAccount != cheque.BankAccountId)
                await ChequeLedger.RecalculateBalanceAsync(db, previousAccount, DateTime.Now);

            return Results.Ok(cheque);
        }).RequireAuthorization(Policies.AnyUser);

        group.MapDelete("/{id:int}", async (int id, SadrDbContext db, CancellationToken ct) =>
        {
            var cheque = await db.Cheques.FirstOrDefaultAsync(c => c.Id == id, ct);
            if (cheque is null) return Results.NotFound();

            cheque.Deleted = true;
            var rows = await db.BankTransactions.Where(t => t.ChequeId == id && !t.Deleted).ToListAsync(ct);
            foreach (var row in rows) row.Deleted = true;
            await db.SaveChangesAsync(ct);
            await ChequeLedger.RecalculateBalanceAsync(db, cheque.BankAccountId, DateTime.Now);
            return Results.NoContent();
        }).RequireAuthorization(Policies.AnyUser);

        /// Status transition: cashed/spent post to the ledger, bounced reverses it,
        /// in-hand/passed-to-bank/canceled reverse any posting. Idempotent.
        group.MapPost("/{id:int}/status", async (int id, ChequeStatusInput body, SadrDbContext db, CancellationToken ct) =>
        {
            var cheque = await db.Cheques.FirstOrDefaultAsync(c => c.Id == id && !c.Deleted, ct);
            if (cheque is null) return Results.NotFound();
            if (!ChequeConsts.StatusNames.ContainsKey(body.Status))
                return Results.BadRequest(new { message = "وضعیت چک نامعتبر است." });

            cheque.Status = body.Status;
            await db.SaveChangesAsync(ct);

            switch (body.Status)
            {
                case ChequeConsts.StatusCashed:
                    await ChequeLedger.SyncChequeAsync(db, cheque, ChequeLedger.EventCashed, DateTime.Now);
                    break;
                case ChequeConsts.StatusSpent:
                    await ChequeLedger.SyncChequeAsync(db, cheque, ChequeLedger.EventSpent, DateTime.Now);
                    break;
                case ChequeConsts.StatusBounced:
                    await ChequeLedger.SyncChequeAsync(db, cheque, ChequeLedger.EventBounced, DateTime.Now);
                    break;
                default:
                    // Back to in-hand / passed to bank / canceled: remove any posted effect.
                    var posted = await db.BankTransactions.Where(t => t.ChequeId == cheque.Id && !t.Deleted).ToListAsync(ct);
                    foreach (var row in posted) row.Deleted = true;
                    await db.SaveChangesAsync(ct);
                    await ChequeLedger.RecalculateBalanceAsync(db, cheque.BankAccountId, DateTime.Now);
                    break;
            }

            return Results.Ok(new
            {
                cheque.Id, cheque.Status,
                balance = await ChequeLedger.BalanceOfAsync(db, cheque.BankAccountId)
            });
        }).RequireAuthorization(Policies.AnyUser);
    }
}
