using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// ورود جنس: حرکت Receipt و به‌روزرسانی OnHand بچ در یک SaveChanges (یک تراکنش).
public static class ReceiptEndpoints
{
    private const string TwoDates = "یک بچ دو تاریخ ندارد؛ بچ نامبر را بررسی کنید.";
    private const string RaceMessage = "این بچ هم‌زمان ثبت شد؛ دوباره تلاش کنید.";

    public static void MapReceiptEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Receipts).RequireAuthorization(Permissions.StockReceive);
        group.MapPost("", ReceiveAsync);
    }

    private static async Task<IResult> ReceiveAsync(ReceiptRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        var batchNumber = TextNormalizer.Clean(request.BatchNumber);
        var batchKey = TextNormalizer.ScanKey(batchNumber);
        if (batchNumber is null || batchKey is null) return ApiProblems.Validation("BatchNumber", "بچ نامبر را وارد کنید.");
        if (batchNumber.Length > ProductRules.MaxBatch) return ApiProblems.Validation("BatchNumber", $"بچ نامبر حداکثر {ProductRules.MaxBatch} نویسه است.");
        if (request.Quantity <= 0) return ApiProblems.Validation("Quantity", "تعداد باید بیشتر از صفر باشد.");
        if (request.UnitCost is < 0) return ApiProblems.Validation("UnitCost", "قیمت واحد نمی‌تواند منفی باشد.");
        var note = TextNormalizer.Clean(request.Note);
        if (note is { Length: > ProductRules.MaxNote }) return ApiProblems.Validation("Note", $"یادداشت حداکثر {ProductRules.MaxNote} نویسه است.");

        var expiry = ExpiryDate.Parse(request.Expiry);
        if (expiry.Error is { } error) return ApiProblems.Validation("Expiry", error);

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return ApiProblems.NotFound();
        if (!product.IsActive) return ApiProblems.Validation("ProductId", "این قلم غیرفعال است.");

        var all = await db.StorageLocations.AsNoTracking().ToListAsync();
        if (request.LocationId is { } locationId)
        {
            if (all.All(l => l.Id != locationId)) return ApiProblems.Validation("LocationId", "محل پیدا نشد.");
            var usable = LocationTree.EffectivelyActive(all.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
            if (!usable.Contains(locationId)) return ApiProblems.Validation("LocationId", "این محل غیرفعال است.");
        }

        var batch = await db.Batches.FirstOrDefaultAsync(b => b.ProductId == product.Id && b.BatchKey == batchKey);
        var created = batch is null;
        var now = DateTime.UtcNow;
        if (batch is null)
        {
            if (expiry.Value is null && product.RequiresExpiry) return ApiProblems.Validation("Expiry", "تاریخ انقضا را وارد کنید.");

            batch = new Batch
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                BatchNumber = batchNumber,
                BatchKey = batchKey,
                ExpiryDate = expiry.Value?.Date,
                ExpiryPrecision = expiry.Value?.Precision ?? ExpiryPrecision.Day,
                ExpiryCalendar = expiry.Value?.Calendar ?? CalendarKind.Gregorian,
                LocationId = request.LocationId, // null ← محل قلم
                UnitCost = request.UnitCost,
                CreatedAtUtc = now
            };
            db.Batches.Add(batch);
        }
        else
        {
            // تاریخ خالی = همان تاریخ ثبت‌شده؛ هر تاریخ دیگر (حتی وقتی بچ تاریخ ندارد) ناسازگار است.
            if (expiry.Value is { } given && batch.ExpiryDate != given.Date) return ApiProblems.Conflict(TwoDates);
            if (request.BatchVersion is not { } version) return ApiProblems.VersionConflict();
            db.ExpectVersion(batch, version);
        }

        ledger.Post(batch, StockMovementKind.Receipt, request.Quantity, now, note, atUtc: now);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict(RaceMessage);
        }

        var paths = LocationTree.Paths(all.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        return Results.Ok(new ReceiptResult(BatchMapping.ToItem(batch, paths, product.LocationId, BatchMapping.Today()), created, expiry.Warnings));
    }
}
