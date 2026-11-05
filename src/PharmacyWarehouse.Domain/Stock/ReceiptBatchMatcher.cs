using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Stock;

public enum BatchMatchKind { NoMatch, UseExisting, DateMismatch, InvalidExpiry }

public sealed record BatchMatch(BatchMatchKind Kind, BatchItem? Batch = null, string? Message = null);

// تصمیم «بچ تازه» در صفحه‌ی ورود جنس: شماره‌ی تایپ‌شده با بچ موجود یکی است یا نه، و آیا مجاز است به آن برویم.
// قاعده‌ی تاریخ عین ReceiptEndpoints است: تاریخ خالی = همان تاریخ ثبت‌شده؛ هر تاریخ دیگر (حتی وقتی بچ تاریخ ندارد) ناسازگار است.
// سرور مرجع می‌ماند؛ این تابع فقط جلوی دور ریختن بی‌صدای تاریخ واردشده را می‌گیرد.
public static class ReceiptBatchMatcher
{
    public static BatchMatch Resolve(string? typedBatchNumber, string? typedExpiry, IReadOnlyList<BatchItem> batches)
    {
        var key = TextNormalizer.ScanKey(typedBatchNumber);
        if (key is null) return new BatchMatch(BatchMatchKind.NoMatch);

        var existing = batches.FirstOrDefault(b => TextNormalizer.ScanKey(b.BatchNumber) == key);
        if (existing is null) return new BatchMatch(BatchMatchKind.NoMatch);

        var parsed = ExpiryDate.Parse(typedExpiry);
        if (parsed.Error is { } error) return new BatchMatch(BatchMatchKind.InvalidExpiry, Message: error);
        if (parsed.Value is not { } given) return new BatchMatch(BatchMatchKind.UseExisting, existing);
        if (existing.ExpiryDate == given.Date) return new BatchMatch(BatchMatchKind.UseExisting, existing);

        var registered = existing.ExpiryText ?? "بدون تاریخ";
        var message = $"این بچ با تاریخ {registered} ثبت شده و تاریخ واردشده {ExpiryDate.Display(given)} است؛ بچ‌نامبر را بررسی کنید.";
        return new BatchMatch(BatchMatchKind.DateMismatch, Message: message);
    }
}
