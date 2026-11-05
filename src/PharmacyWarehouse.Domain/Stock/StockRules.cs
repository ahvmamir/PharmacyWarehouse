using System.Globalization;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Stock;

public enum DisposalReason { Expired, Damaged, Other }

// برچسب فارسی نوع حرکت، برای جزئیات قلم و گزارش‌ها.
public static class MovementLabels
{
    public static string Kind(StockMovementKind kind) => kind switch
    {
        StockMovementKind.Import => "ورود از Excel",
        StockMovementKind.Receipt => "ورود جنس",
        StockMovementKind.Withdrawal => "برداشت",
        StockMovementKind.Reversal => "برگشت",
        StockMovementKind.Adjustment => "اصلاح",
        StockMovementKind.Disposal => "اسقاط",
        _ => kind.ToString()
    };
}

// قواعد اسقاط. دلیل در یادداشت حرکت ذخیره می‌شود، چون StockMovement ستون جدا برای آن ندارد.
public static class DisposalRules
{
    public static string Label(DisposalReason reason) => reason switch
    {
        DisposalReason.Expired => "منقضی",
        DisposalReason.Damaged => "آسیب‌دیده",
        _ => "دیگر"
    };

    public static string MovementNote(DisposalReason reason, string note) => $"{Label(reason)}: {note}";
}

// کد برچسب: Code128 فقط ASCII دارد و نام فارسی محل در آن جا نمی‌شود.
public static class LocationLabel
{
    private const string Prefix = "LOC-";

    // ۸ رقم اول شناسه، کوتاه برای عرض برچسب؛ برخورد در یک داروخانه عملاً پیش نمی‌آید.
    public static string CodeOf(Guid id) => Prefix + id.ToString("N")[..8].ToUpperInvariant();

    public static bool Matches(Guid id, string? scanned) => TextNormalizer.ScanKey(scanned) == CodeOf(id);
}

// کد داخلی قلم بدون بارکد کارخانه (بخش ۹.۳ spec): PW- و شماره؛ در ProductBarcode ثبت می‌شود.
public static class InternalBarcode
{
    public const string Prefix = "PW-";

    public static string Format(int number) => Prefix + number.ToString("00000", CultureInfo.InvariantCulture);

    public static bool IsInternal(string? codeKey) => codeKey is not null && codeKey.StartsWith(Prefix, StringComparison.Ordinal);

    // بزرگ‌ترین شماره به‌علاوه‌ی یک؛ کدی که بعد از پیشوند عدد نیست نادیده گرفته می‌شود.
    public static int NextNumber(IEnumerable<string> codeKeys)
    {
        var max = 0;
        foreach (var key in codeKeys)
        {
            if (IsInternal(key) && int.TryParse(key.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > max)
            {
                max = number;
            }
        }

        return max + 1;
    }
}
