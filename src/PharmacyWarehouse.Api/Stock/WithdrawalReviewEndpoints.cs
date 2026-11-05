using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Stock;

// بررسی برداشت (بخش ۷.۲ spec): تأیید فقط Status را عوض می‌کند؛ رد برای هر حرکت برداشت یک Reversal ثبت می‌کند.
public static class WithdrawalReviewEndpoints
{
    private const int MaxListTake = 200;
    private const string AlreadyReviewed = "این برداشت قبلاً بررسی شده است.";

    public static void MapWithdrawalReviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Withdrawals).RequireAuthorization(Permissions.WithdrawalReview);
        group.MapGet("review", ListAsync);
        group.MapPost("{id:guid}/approve", ApproveAsync);
        group.MapPost("{id:guid}/reject", RejectAsync);
        group.MapPost("approve", BulkApproveAsync);
    }

    // status: Pending (پیش‌فرض)، Approved، Rejected یا all. بررسی‌نشده‌ها قدیمی‌ترین اول (نوبت)، بقیه تازه‌ترین اول.
    private static async Task<IResult> ListAsync(WarehouseDbContext db, string? status, int take = 100)
    {
        take = Math.Clamp(take, 1, MaxListTake);
        var query = db.Withdrawals.AsNoTracking().AsQueryable();
        var pending = true;
        if (!string.IsNullOrEmpty(status) && !status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
        {
            pending = false;
            if (!status.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<WithdrawalStatus>(status, ignoreCase: true, out var parsed)) return ApiProblems.Validation("Status", "وضعیت نامعتبر است.");
                query = query.Where(w => w.Status == parsed);
            }
        }
        else
        {
            query = query.Where(w => w.Status == WithdrawalStatus.Pending);
        }

        var rows = await query.ToListAsync();
        var page = (pending
            ? rows.OrderBy(w => w.AtUtc).ThenBy(w => w.Id)
            : rows.OrderByDescending(w => w.AtUtc).ThenByDescending(w => w.Id)).Take(take).ToList();
        return Results.Ok(await WithdrawalEndpoints.ToItemsAsync(db, page));
    }

    private static async Task<IResult> ApproveAsync(Guid id, ReviewRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var withdrawal = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id);
        if (withdrawal is null) return ApiProblems.NotFound();
        if (withdrawal.Status != WithdrawalStatus.Pending) return ApiProblems.Conflict(AlreadyReviewed);

        db.ExpectVersion(withdrawal, request.Version);
        MarkReviewed(withdrawal, WithdrawalStatus.Approved, userId, null);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok((await WithdrawalEndpoints.ToItemsAsync(db, [withdrawal])).Single());
    }

    private static async Task<IResult> RejectAsync(Guid id, ReviewRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var note = TextNormalizer.Clean(request.Note);
        if (note is null) return ApiProblems.Validation("Note", "برای رد کردن، یادداشت لازم است.");
        if (note.Length > Withdrawal.MaxReviewNote) return ApiProblems.Validation("Note", $"یادداشت حداکثر {Withdrawal.MaxReviewNote} نویسه است.");

        var withdrawal = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == id);
        if (withdrawal is null) return ApiProblems.NotFound();
        if (withdrawal.Status != WithdrawalStatus.Pending) return ApiProblems.Conflict(AlreadyReviewed);

        db.ExpectVersion(withdrawal, request.Version);
        MarkReviewed(withdrawal, WithdrawalStatus.Rejected, userId, note);
        audit.Reason = note; // در تاریخچه‌ی همین برداشت دیده می‌شود

        var movements = await db.StockMovements.Where(m => m.WithdrawalId == id && m.Kind == StockMovementKind.Withdrawal).ToListAsync();
        var batchIds = movements.Select(m => m.BatchId).ToList();
        var batches = await db.Batches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);

        // موجودی فقط برمی‌گردد (مثبت)، پس منفی‌شدن ممکن نیست؛ Version بچ را رهگیر خودش جلو می‌برد.
        foreach (var movement in movements)
        {
            ledger.Post(batches[movement.BatchId], StockMovementKind.Reversal, -movement.Quantity, DateTime.UtcNow, note, withdrawal.Id, movement.Id);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok((await WithdrawalEndpoints.ToItemsAsync(db, [withdrawal])).Single());
    }

    private static async Task<IResult> BulkApproveAsync(BulkApproveRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        if (request.Items is null || request.Items.Count == 0) return ApiProblems.Validation("Items", "حداقل یک برداشت انتخاب کنید.");
        if (request.Items.Count > MaxListTake) return ApiProblems.Validation("Items", $"حداکثر {MaxListTake} برداشت در هر بار.");

        var approved = new List<Guid>();
        var failed = new List<BulkFailure>();
        foreach (var target in request.Items.DistinctBy(t => t.Id))
        {
            // هر برداشت جدا ذخیره می‌شود؛ پس از هر شکست، ردیابی پاک می‌شود تا ردیف نیمه‌کاره نماند.
            db.ChangeTracker.Clear();
            var withdrawal = await db.Withdrawals.FirstOrDefaultAsync(w => w.Id == target.Id);
            if (withdrawal is null) { failed.Add(new BulkFailure(target.Id, "برداشت پیدا نشد.")); continue; }
            if (withdrawal.Status != WithdrawalStatus.Pending) { failed.Add(new BulkFailure(target.Id, AlreadyReviewed)); continue; }

            db.ExpectVersion(withdrawal, target.Version);
            MarkReviewed(withdrawal, WithdrawalStatus.Approved, userId, null);
            try
            {
                await db.SaveChangesAsync();
                approved.Add(target.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                failed.Add(new BulkFailure(target.Id, "این برداشت در این فاصله تغییر کرده است؛ فهرست را تازه کنید."));
            }
        }

        db.ChangeTracker.Clear();
        return Results.Ok(new BulkApproveResult(approved, failed));
    }

    private static void MarkReviewed(Withdrawal withdrawal, WithdrawalStatus status, Guid reviewerId, string? note)
    {
        withdrawal.Status = status;
        withdrawal.ReviewedById = reviewerId;
        withdrawal.ReviewedAtUtc = DateTime.UtcNow;
        withdrawal.ReviewNote = note;
        // [تصمیم] انباردار برداشت خودش را می‌تواند بررسی کند؛ علامت می‌خورد و در داشبورد دیده می‌شود.
        withdrawal.SelfReviewed = withdrawal.UserId == reviewerId;
    }
}
