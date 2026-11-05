namespace PharmacyWarehouse.Domain.Stock;

public enum ExpiryBucket { None, Expired, Near, Far }

// قواعد داشبورد و آستانه‌های انقضا (بخش ۱۰ spec).
public static class DashboardRules
{
    public const string NearMonthsKey = "expiry.nearMonths";
    public const string FarMonthsKey = "expiry.farMonths";
    public const int DefaultNearMonths = 3;
    public const int DefaultFarMonths = 6;
    public const int MaxMonths = 60;

    // بچ بی‌تاریخ یا دورتر از آستانه‌ی دوم: None. روز انقضا هنوز منقضی نیست (همان ExpiryDate.IsExpired).
    public static ExpiryBucket Classify(DateOnly? expiry, DateOnly today, int nearMonths, int farMonths)
    {
        if (expiry is not { } date) return ExpiryBucket.None;
        if (date < today) return ExpiryBucket.Expired;
        if (date <= today.AddMonths(nearMonths)) return ExpiryBucket.Near;
        return date <= today.AddMonths(farMonths) ? ExpiryBucket.Far : ExpiryBucket.None;
    }

    // null یعنی معتبر؛ در غیر این صورت پیام فارسی.
    public static string? ValidateThresholds(int nearMonths, int farMonths)
    {
        if (nearMonths < 1 || farMonths < 1 || nearMonths > MaxMonths || farMonths > MaxMonths)
            return $"آستانه باید بین ۱ و {MaxMonths} ماه باشد.";
        return nearMonths < farMonths ? null : "آستانه‌ی اول باید از آستانه‌ی دوم کوچک‌تر باشد.";
    }

    // MinStock خالی یعنی بدون هشدار؛ موجودی ۰ با MinStock مثبت هم هشدار است.
    public static bool IsBelowMinimum(int? minStock, int unexpiredOnHand) => minStock is { } min && unexpiredOnHand < min;
}
