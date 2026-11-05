namespace PharmacyWarehouse.Domain.Stock;

// زمان برداشت فیزیکی (بخش ۷.۵ spec). همه‌ی زمان‌ها UTC.
public sealed record WithdrawalTime(DateTime OccurredAtUtc, bool IsLate, string? Error);

public static class WithdrawalRules
{
    public const int MaxBackdateDays = 7;
    public static readonly TimeSpan LateAfter = TimeSpan.FromHours(2);

    // ساعت گوشی ممکن است کمی جلوتر از سرور باشد؛ تا این حد آینده به «اکنون» گرد می‌شود، بیشتر رد می‌شود.
    public static readonly TimeSpan FutureSkew = TimeSpan.FromMinutes(5);

    public const string FutureError = "زمان برداشت نمی‌تواند در آینده باشد.";
    public const string TooOldError = "زمان برداشت بیش از ۷ روز قبل است؛ به انباردار اطلاع دهید.";

    public static WithdrawalTime ResolveTime(DateTime? requestedUtc, DateTime nowUtc)
    {
        if (requestedUtc is not { } requested) return new WithdrawalTime(nowUtc, false, null);

        // Unspecified را UTC فرض می‌کنیم؛ JSON کلاینت همیشه Z دارد.
        var occurred = requested.Kind == DateTimeKind.Local ? requested.ToUniversalTime() : DateTime.SpecifyKind(requested, DateTimeKind.Utc);

        if (occurred > nowUtc)
        {
            return occurred - nowUtc <= FutureSkew
                ? new WithdrawalTime(nowUtc, false, null)
                : new WithdrawalTime(nowUtc, false, FutureError);
        }

        if (nowUtc - occurred > TimeSpan.FromDays(MaxBackdateDays)) return new WithdrawalTime(nowUtc, false, TooOldError);

        return new WithdrawalTime(occurred, nowUtc - occurred > LateAfter, null);
    }
}
