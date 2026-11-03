using SadrWeb.Api.Data;
using SadrWeb.Api.Infrastructure;

namespace SadrWeb.Api.Endpoints;

/// <summary>
/// Everything a client needs to render Persian labels and dropdowns without hard-coding
/// the business constants: the same values the desktop app uses.
/// </summary>
public static class MetaEndpoints
{
    public static void MapMetaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/meta", () => Results.Ok(new
        {
            roles = new[] { RoleConsts.Admin, RoleConsts.Warehouse, RoleConsts.Sales, RoleConsts.User },
            invoiceTypes = InvoiceTypeConsts.Sell == 0
                ? new[] { new { value = 0, label = "فاکتور فروش" }, new { value = 1, label = "فاکتور خرید" }, new { value = 2, label = "پیش‌فاکتور" } }
                : Array.Empty<object>(),
            invoiceStatuses = new[]
            {
                new { value = InvoiceStatusConsts.Draft, label = "پیش‌نویس" },
                new { value = InvoiceStatusConsts.Issued, label = "صادر شده" },
                new { value = InvoiceStatusConsts.Confirmed, label = "تأیید شده" },
                new { value = InvoiceStatusConsts.Cancelled, label = "لغو شده" }
            },
            invoiceLayouts = new[]
            {
                new { value = InvoiceLayoutConsts.Classic, label = "کلاسیک رسمی" },
                new { value = InvoiceLayoutConsts.Modern, label = "ساده و مدرن" },
                new { value = InvoiceLayoutConsts.Compact, label = "کامپکت" }
            },
            chequeDirections = new[]
            {
                new { value = ChequeConsts.DirectionReceived, label = "دریافتی" },
                new { value = ChequeConsts.DirectionIssued, label = "پرداختی" }
            },
            chequeStatuses = ChequeConsts.StatusNames.Select(kv => new { value = kv.Key, label = kv.Value }),
            transferDirections = new[]
            {
                new { value = WarehouseTransfer.DirectionIn, label = "ورود" },
                new { value = WarehouseTransfer.DirectionOut, label = "خروج" }
            },
            taskStatuses = new[]
            {
                new { value = TaskStatusConsts.NotStarted, label = "شروع نشده" },
                new { value = TaskStatusConsts.InProgress, label = "در حال اجرا" },
                new { value = TaskStatusConsts.Paused, label = "متوقف" },
                new { value = TaskStatusConsts.Done, label = "انجام شده" },
                new { value = TaskStatusConsts.Failed, label = "ناموفق" }
            },
            accountTypes = new[]
            {
                new { value = AccountTypeConsts.Debit, label = "بدهکار" },
                new { value = AccountTypeConsts.Credit, label = "بستانکار" },
                new { value = AccountTypeConsts.Both, label = "جهت‌دار (هر دو)" }
            },
            transactionTypes = new[]
            {
                new { value = AccountTransactionTypeConsts.Receipt, label = "دریافت" },
                new { value = AccountTransactionTypeConsts.Payment, label = "پرداخت" },
                new { value = AccountTransactionTypeConsts.Transfer, label = "انتقال" }
            },
            transactionSystems = new[]
            {
                new { value = TransactionSystemConsts.Cash, label = "صندوق" },
                new { value = TransactionSystemConsts.Bank, label = "بانک" }
            },
            bankTransactionKinds = new[]
            {
                new { value = BankTransaction.KindDeposit, label = "واریز" },
                new { value = BankTransaction.KindWithdrawal, label = "برداشت" }
            },
            permissions = new
            {
                canAcceptTransfers = new[] { RoleConsts.Admin, RoleConsts.Warehouse },
                canRegisterInvoices = new[] { RoleConsts.Admin, RoleConsts.Sales, RoleConsts.User },
                canManageUsers = new[] { RoleConsts.Admin }
            },
            defaultWarehouseName = DefaultWarehouse.Name
        })).WithTags("meta").AllowAnonymous();

        app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).WithTags("meta").AllowAnonymous();
    }
}
