using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Stock;

// شمارنده‌ی «کارتابل» در منو: تعداد برداشت‌ها و ورودهای در انتظار بررسی.
// فقط خواندنی است؛ هر عدد فقط با مجوز همان صف برمی‌گردد تا کاربر بی‌مجوز از وجود صف چیزی نفهمد.
public static class InboxEndpoints
{
    public static void MapInboxEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Inbox, CountsAsync).RequireAuthorization();
    }

    private static async Task<IResult> CountsAsync(WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        int? withdrawals = null;
        int? receipts = null;

        if (await permissions.HasAsync(audit.UserId, Permissions.WithdrawalReview))
            withdrawals = await db.Withdrawals.CountAsync(w => w.Status == WithdrawalStatus.Pending);

        if (await permissions.HasAsync(audit.UserId, Permissions.StockReceive))
            receipts = await db.Receipts.CountAsync(r => r.Status == ReceiptStatus.Pending);

        return Results.Ok(new InboxCounts { PendingWithdrawals = withdrawals, PendingReceipts = receipts });
    }
}
