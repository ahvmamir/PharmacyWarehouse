using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// نگاشت مشترک بچ به BatchItem برای جزئیات قلم و پاسخ ورود جنس.
internal static class BatchMapping
{
    // «امروز» به وقت محلی (LocalDay)، نه UTC.
    public static DateOnly Today() => LocalDay.Today(DateTime.UtcNow);

    // محل بچ؛ اگر ندارد، محل قلم نمایش داده می‌شود.
    public static BatchItem ToItem(Batch b, IReadOnlyDictionary<Guid, string> paths, Guid? productLocationId, DateOnly today)
    {
        string? PathOf(Guid? id) => id is { } g && paths.TryGetValue(g, out var p) ? p : null;
        return new BatchItem(b.Id, b.BatchNumber, b.ExpiryDate, b.ExpiryPrecision, b.ExpiryCalendar,
            b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null,
            PathOf(b.LocationId) ?? PathOf(productLocationId), b.OnHand, ExpiryDate.IsExpired(b.ExpiryDate, today), b.Version);
    }
}
