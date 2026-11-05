using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;

namespace PharmacyWarehouse.Api.Operations;

// ثابت OnHand = جمع حرکت‌ها را می‌سنجد و فقط گزارش می‌دهد؛ هرگز چیزی را خودکار تغییر نمی‌دهد (بخش ۴.۲ spec).
public static class StockVerifier
{
    public sealed record Mismatch(Guid BatchId, string BatchNumber, int OnHand, int MovementSum);

    public static async Task<(int Checked, List<Mismatch> Mismatches)> VerifyAsync(WarehouseDbContext db)
    {
        var sums = await db.StockMovements.AsNoTracking()
            .GroupBy(m => m.BatchId)
            .Select(g => new { BatchId = g.Key, Sum = g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(g => g.BatchId, g => g.Sum);
        var batches = await db.Batches.AsNoTracking().Select(b => new { b.Id, b.BatchNumber, b.OnHand }).ToListAsync();

        var mismatches = batches
            .Where(b => b.OnHand != sums.GetValueOrDefault(b.Id))
            .Select(b => new Mismatch(b.Id, b.BatchNumber, b.OnHand, sums.GetValueOrDefault(b.Id)))
            .ToList();
        return (batches.Count, mismatches);
    }
}
