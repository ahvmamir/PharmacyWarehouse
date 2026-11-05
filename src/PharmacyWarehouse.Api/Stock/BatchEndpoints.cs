using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Stock;

// اصلاح شمارش و اسقاط (بخش ۷.۴ spec): حرکت و OnHand در یک SaveChanges. قلم غیرفعال هم قابل اصلاح است، چون موجودی‌اش واقعی است.
public static class BatchEndpoints
{
    public static void MapBatchEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Batches).RequireAuthorization(Permissions.StockAdjust);
        group.MapPost("{id:guid}/adjust", AdjustAsync);
        group.MapPost("{id:guid}/dispose", DisposeAsync);
    }

    private static async Task<IResult> AdjustAsync(Guid id, AdjustBatchRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        if (request.Counted < 0) return ApiProblems.Validation("Counted", "تعداد شمرده‌شده نمی‌تواند منفی باشد.");
        var note = TextNormalizer.Clean(request.Note);
        if (note is null) return ApiProblems.Validation("Note", "یادداشت اصلاح اجباری است.");
        if (note.Length > ProductRules.MaxNote) return ApiProblems.Validation("Note", $"یادداشت حداکثر {ProductRules.MaxNote} نویسه است.");

        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null) return ApiProblems.NotFound();
        if (batch.Version != request.Version) return ApiProblems.VersionConflict();

        var difference = request.Counted - batch.OnHand;
        if (difference == 0) return ApiProblems.Validation("Counted", "این تعداد با موجودی فعلی برابر است؛ اصلاحی لازم نیست.");

        db.ExpectVersion(batch, request.Version);
        ledger.Post(batch, StockMovementKind.Adjustment, difference, DateTime.UtcNow, note);
        return await SaveAsync(db, batch);
    }

    private static async Task<IResult> DisposeAsync(Guid id, DisposeBatchRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        if (request.Quantity <= 0) return ApiProblems.Validation("Quantity", "تعداد باید بیشتر از صفر باشد.");
        if (!Enum.IsDefined(request.Reason)) return ApiProblems.Validation("Reason", "دلیل اسقاط نامعتبر است.");
        var note = TextNormalizer.Clean(request.Note);
        if (note is null) return ApiProblems.Validation("Note", "یادداشت اسقاط اجباری است.");

        var movementNote = DisposalRules.MovementNote(request.Reason, note);
        if (movementNote.Length > ProductRules.MaxNote) return ApiProblems.Validation("Note", "یادداشت طولانی است.");

        var batch = await db.Batches.FirstOrDefaultAsync(b => b.Id == id);
        if (batch is null) return ApiProblems.NotFound();
        if (batch.Version != request.Version) return ApiProblems.VersionConflict();
        if (request.Quantity > batch.OnHand) return ApiProblems.Validation("Quantity", $"حداکثر {batch.OnHand} جعبه موجود است.");

        db.ExpectVersion(batch, request.Version);
        ledger.Post(batch, StockMovementKind.Disposal, -request.Quantity, DateTime.UtcNow, movementNote);
        return await SaveAsync(db, batch);
    }

    private static async Task<IResult> SaveAsync(WarehouseDbContext db, Batch batch)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        var productLocation = await db.Products.AsNoTracking().Where(p => p.Id == batch.ProductId).Select(p => p.LocationId).FirstAsync();
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        return Results.Ok(BatchMapping.ToItem(batch, paths, productLocation, BatchMapping.Today()));
    }
}
