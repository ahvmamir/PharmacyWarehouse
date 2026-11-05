using System.Globalization;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Stock;

// برچسب فارسی موجودیت‌ها، کارها و فیلدهای تاریخچه، و خوانا کردن مقدارهای ذخیره‌شده.
public static class AuditLabels
{
    private const string Empty = "—";

    private static readonly Dictionary<string, string> Entities = new()
    {
        ["Product"] = "قلم",
        ["Batch"] = "بچ",
        ["ProductBarcode"] = "بارکد",
        ["StorageLocation"] = "محل",
        ["Withdrawal"] = "برداشت"
    };

    private static readonly Dictionary<string, string> Actions = new()
    {
        ["Created"] = "ثبت",
        ["Updated"] = "ویرایش",
        ["Deleted"] = "حذف"
    };

    // فیلدهای قابل‌ثبت در تاریخچه (کلیدها، Id، Version و OnHand ثبت نمی‌شوند).
    private static readonly Dictionary<string, string> Fields = new()
    {
        ["Name"] = "نام",
        ["Brand"] = "برند",
        ["PackDescription"] = "بسته‌بندی",
        ["LocationId"] = "محل",
        ["MinStock"] = "حداقل موجودی",
        ["UnitsPerPack"] = "تعداد در بسته",
        ["RequiresExpiry"] = "نیاز به تاریخ انقضا",
        ["AllowExpiredWithdrawal"] = "برداشت منقضی مجاز",
        ["IsActive"] = "فعال",
        ["ProductId"] = "قلم",
        ["Code"] = "کد بارکد",
        ["BatchNumber"] = "شماره‌ی بچ",
        ["ExpiryDate"] = "تاریخ انقضا",
        ["ExpiryPrecision"] = "دقت انقضا",
        ["ExpiryCalendar"] = "تقویم انقضا",
        ["UnitCost"] = "قیمت واحد",
        ["ParentId"] = "والد",
        ["SortOrder"] = "ترتیب",
        ["UserId"] = "ثبت‌کننده",
        ["AtUtc"] = "زمان ثبت",
        ["OccurredAtUtc"] = "زمان برداشت",
        ["IsLate"] = "ثبت دیرهنگام",
        ["AfterStocktake"] = "پس از انبارگردانی",
        ["Status"] = "وضعیت",
        ["ReviewedById"] = "بررسی‌کننده",
        ["ReviewedAtUtc"] = "زمان بررسی",
        ["ReviewNote"] = "یادداشت بررسی",
        ["SelfReviewed"] = "بررسی توسط خود ثبت‌کننده"
    };

    public static string Entity(string? entityType) =>
        entityType is not null && Entities.TryGetValue(entityType, out var label) ? label : entityType ?? "";

    public static string Action(string? action) =>
        action is not null && Actions.TryGetValue(action, out var label) ? label : action ?? "";

    public static string Field(string? field) =>
        field is not null && Fields.TryGetValue(field, out var label) ? label : field ?? "";

    // مقدار ذخیره‌شده را خوانا می‌کند. شناسه‌ها به شکل «guid|name» ذخیره می‌شوند؛ فقط نام نمایش داده می‌شود.
    public static string Value(string? field, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return Empty;

        // پیشوند شناسه فقط وقتی حذف می‌شود که واقعاً Guid باشد، تا «|» داخل متن آزاد خراب نشود.
        if (raw.Length > 37 && raw[36] == '|' && Guid.TryParse(raw.AsSpan(0, 36), out _)) return raw[37..];

        if (bool.TryParse(raw, out var flag)) return flag ? "بله" : "خیر";

        switch (field)
        {
            case "ExpiryPrecision" when Enum.TryParse<ExpiryPrecision>(raw, out var precision):
                return precision == ExpiryPrecision.Day ? "روز" : "ماه";
            case "ExpiryCalendar" when Enum.TryParse<CalendarKind>(raw, out var calendar):
                return calendar == CalendarKind.Jalali ? "شمسی" : "میلادی";
            case "ExpiryDate" when DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var date):
                return ExpiryDate.Gregorian(new ExpiryValue(date, ExpiryPrecision.Day, CalendarKind.Gregorian));
            case "Status" when Enum.TryParse<WithdrawalStatus>(raw, out var status):
                return status switch { WithdrawalStatus.Pending => "تأییدنشده", WithdrawalStatus.Approved => "تأییدشده", _ => "ردشده" };
            case "UnitCost" when long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cost):
                return cost.ToString("N0", CultureInfo.InvariantCulture);
            default:
                return raw;
        }
    }

    // تاریخ انقضای ردیف «ثبت» با همان تقویم و دقت ورودی نمایش داده می‌شود؛ ورودی نامعتبر ← «—».
    public static string Expiry(string? date, string? precision, string? calendar)
    {
        if (!DateOnly.TryParse(date, CultureInfo.InvariantCulture, out var parsed)) return Empty;

        var value = new ExpiryValue(
            parsed,
            Enum.TryParse<ExpiryPrecision>(precision, out var p) ? p : ExpiryPrecision.Day,
            Enum.TryParse<CalendarKind>(calendar, out var c) ? c : CalendarKind.Gregorian);
        return ExpiryDate.Display(value);
    }
}
