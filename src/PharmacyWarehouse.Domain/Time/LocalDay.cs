namespace PharmacyWarehouse.Domain.Time;

// مرز «امروز» برای نمایش. همه‌ی زمان‌ها UTC ذخیره می‌شوند؛ این فقط هنگام خواندن اعمال می‌شود.
// آفست ثابت، نه TimeZoneInfo، تا به داده‌ی منطقه‌ی سیستم‌عامل تکیه نکنیم.
// فرض: ایران ساعت تابستانی ندارد. با جست‌وجوی وب (نوامبر ۲۰۲۶) تأیید شد: آخرین تغییر ساعت ۲۱ سپتامبر ۲۰۲۲
// بوده و ۲۰۲۶ هم تغییری نیست. اگر قانون بعداً عوض شود، آفست ثابت آن را نمی‌بیند و فقط مرز روز جابه‌جا می‌شود.
public static class LocalDay
{
    public static readonly TimeSpan OffsetFromUtc = new(3, 30, 0);

    // [StartUtc, EndUtc): نیمه‌شب محلی تا نیمه‌شب محلی بعد.
    public static (DateTimeOffset StartUtc, DateTimeOffset EndUtc) BoundsUtc(DateTimeOffset nowUtc)
    {
        var local = nowUtc.UtcDateTime + OffsetFromUtc;
        var startUtc = DateTime.SpecifyKind(local.Date - OffsetFromUtc, DateTimeKind.Utc);

        return (new DateTimeOffset(startUtc), new DateTimeOffset(startUtc.AddDays(1)));
    }
}