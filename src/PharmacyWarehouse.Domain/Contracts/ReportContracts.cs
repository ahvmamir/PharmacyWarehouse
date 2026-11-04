using PharmacyWarehouse.Domain.Assets;

namespace PharmacyWarehouse.Domain.Contracts;

public sealed record ReportInfo(string Key, string Title, string Description, bool UsesDays, bool UsesFilters);

public static class ReportCatalog
{
    public const string Equipment = "equipment";
    public const string ByOwner = "by-owner";
    public const string ByLocation = "by-location";
    public const string ByTypeStatus = "by-type-status";
    public const string TemporaryAssets = "temporary-assets";
    public const string Components = "components";
    public const string RecentChanges = "recent-changes";
    public const string NotVerified = "not-verified";
    public const string Repairs = "repairs";

    public static readonly IReadOnlyList<ReportInfo> All =
    [
        new(RecentChanges, "آخرین تغییرات", "چه کسی، چه زمانی، چه چیزی را عوض کرده است.", true, false),
        new(Equipment, "فهرست تجهیزات", "همه‌ی دستگاه‌ها با مشخصات، محل و آخرین تغییر.", false, true),
        new(ByOwner, "تعداد به تفکیک واحد صاحب اموال", "هر واحد چند دستگاه از هر نوع دارد.", false, false),
        new(ByLocation, "تعداد به تفکیک محل", "در هر ساختمان، طبقه و واحد چند دستگاه هست.", false, false),
        new(ByTypeStatus, "تعداد به تفکیک نوع و وضعیت", "نمای کلی موجودی.", false, false),
        new(TemporaryAssets, "دستگاه‌های با شماره موقت", "شماره‌ی واقعی این‌ها باید پیدا و ثبت شود.", false, false),
        new(Components, "فهرست قطعات", "قطعات کیس‌ها و لپ‌تاپ‌ها.", false, false),
        new(NotVerified, "دیده‌نشده در انبارگردانی", "دستگاه‌هایی که در این بازه حضورشان تأیید نشده است.", true, false),
        new(Repairs, "سابقه‌ی تعمیرات", "دستگاه‌هایی که در این بازه به تعمیر رفته‌اند.", true, false)
    ];
}

// یک قالب برای همه‌ی گزارش‌ها: همان جدول در صفحه، Excel و چاپ.
public sealed class ReportTable
{
    public string Title { get; init; } = "";
    public string[] Columns { get; init; } = [];
    public List<string?[]> Rows { get; init; } = [];
    public DateTime GeneratedAtUtc { get; init; }
}

public sealed class ImportRowResult
{
    public int Row { get; init; }
    public string Summary { get; init; } = "";
    public string[] Errors { get; init; } = [];
}

public sealed class ImportResult
{
    public bool Committed { get; init; }
    public int Total { get; init; }
    public int Valid { get; init; }
    public int Imported { get; init; }
    public string[] WillCreate { get; init; } = [];
    public List<ImportRowResult> Problems { get; init; } = [];

    // ستون‌هایی که برای نوع‌های انتخاب‌شده معنی دارند؛ کلاینت ردیف‌ها را با همین‌ها نشان می‌دهد و ویرایش می‌کند.
    public string[] Columns { get; init; } = [];

    // همه‌ی ردیف‌ها با مقدار و خطا، تا ردیف خطادار در مرورگر اصلاح و دوباره بررسی شود.
    public List<ImportRow> Rows { get; init; } = [];
}

// یک ردیف ورود: مقدارها با نام ستون Excel (مثل «شماره اموال») کلید خورده‌اند.
public sealed class ImportRow
{
    public int Row { get; set; }
    public Dictionary<string, string?> Values { get; set; } = [];
    public string[] Errors { get; set; } = [];
}

// بررسی یا ثبت ردیف‌هایی که در مرورگر اصلاح شده‌اند، بدون فرستادن دوباره‌ی فایل.
public sealed class ImportRowsRequest
{
    public List<ImportRow> Rows { get; set; } = [];
    public bool CreateMissing { get; set; }

    // خالی یعنی همه‌ی نوع‌ها؛ یک نوع یعنی ستون «نوع» لازم نیست.
    public EquipmentType[] Types { get; set; } = [];
}

// آخرین تغییرات، گروه‌بندی‌شده: هر کار کاربر یک ردیف (مثلاً «ثبت کیس با ۵ قطعه»)، با جزئیات.
public sealed class ChangeSetItem
{
    public Guid Id { get; init; }
    public DateTime AtUtc { get; init; }
    public string UserName { get; init; } = "";
    public string Summary { get; init; } = "";
    public string? Reason { get; init; }

    // اگر کار روی یک دستگاه موجود بوده، برای لینک به صفحه‌ی آن.
    public Guid? EquipmentId { get; init; }
    public List<ChangeLine> Lines { get; init; } = [];
}

public sealed class ChangeLine
{
    public string? Subject { get; init; }
    public string Action { get; init; } = "";
    public string? Field { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
}
