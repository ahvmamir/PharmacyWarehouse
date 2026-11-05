namespace PharmacyWarehouse.Domain.Stock;

public sealed record AllocatableBatch(Guid Id, string BatchNumber, DateOnly? ExpiryDate, DateTime CreatedAtUtc, int OnHand);

public sealed record AllocationLine(Guid BatchId, int Quantity);

// Shortfall > 0 یعنی موجودی قابل‌استفاده کم است؛ Lines همه‌ی آنچه ممکن بود را برمی‌دارد.
public sealed record AllocationResult(IReadOnlyList<AllocationLine> Lines, int Shortfall);

// تنها جای منطق تقسیم برداشت (FEFO). روش دیگر در آینده = یک شاخه‌ی تازه در Allocate؛ endpoint و صفحه عوض نمی‌شوند.
public static class BatchAllocator
{
    // بچ‌های قابل‌پیشنهاد: موجودی دارند و منقضی نیستند (بچ منقضی هرگز پیشنهاد نمی‌شود، حتی با AllowExpiredWithdrawal).
    // ترتیب: زودتر منقضی، بعد زودتر ساخته‌شده، بعد Id؛ بدون تاریخ آخر.
    public static IReadOnlyList<AllocatableBatch> FefoOrder(IEnumerable<AllocatableBatch> batches, DateOnly today) =>
        batches
            .Where(b => b.OnHand > 0 && (b.ExpiryDate is null || b.ExpiryDate >= today))
            .OrderBy(b => b.ExpiryDate is null)
            .ThenBy(b => b.ExpiryDate)
            .ThenBy(b => b.CreatedAtUtc)
            .ThenBy(b => b.Id)
            .ToList();

    public static AllocationResult Allocate(IEnumerable<AllocatableBatch> batches, int quantity, DateOnly today)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "تعداد باید مثبت باشد.");

        var lines = new List<AllocationLine>();
        var remaining = quantity;
        foreach (var batch in FefoOrder(batches, today))
        {
            if (remaining == 0) break;
            var take = Math.Min(batch.OnHand, remaining);
            lines.Add(new AllocationLine(batch.Id, take));
            remaining -= take;
        }

        return new AllocationResult(lines, remaining);
    }
}
