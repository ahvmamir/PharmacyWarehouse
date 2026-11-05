using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Import;

// یک ردیف Excel، همه‌ی خانه‌ها متن (همان چیزی که کاربر در پیش‌نمایش می‌بیند و اصلاح می‌کند).
// Sheet نام شیت (= محل یا گروه) است؛ Location اختیاری و مسیر کامل محل، که بر Sheet مقدم است.
public sealed class ImportRowInput
{
    public string Sheet { get; set; } = "";
    public int RowNumber { get; set; }
    public string? Name { get; set; }
    public string? Brand { get; set; }
    public string? Quantity { get; set; }
    public string? UnitsPerPack { get; set; }
    public string? BatchNumber { get; set; }
    public string? Expiry { get; set; }
    public string? Location { get; set; }
    public string? Barcode { get; set; }
    public string? MinStock { get; set; }
    public string? UnitCost { get; set; }
    // «انقضا لازم نیست» برای این قلم.
    public bool ExpiryNotRequired { get; set; }
}

public enum ImportSeverity { Ok, Warning, Error, Ignored }

public sealed record ImportRowStatus(int Index, ImportSeverity Severity, IReadOnlyList<string> Messages);

// ردیف معتبر (Ok یا Warning) به شکل نرمال‌شده؛ سرور فقط همین را ثبت می‌کند.
public sealed record ImportPlanRow(
    int Index, string Sheet, string? LocationPath, string Name, string? Brand, string? Pack, string ProductKey, bool RequiresExpiry,
    int Quantity, string? BatchNumber, string? BatchKey, ExpiryValue? Expiry,
    string? Barcode, string? BarcodeKey, int? MinStock, long? UnitCost);

public sealed record ImportSummary(
    int TotalRows, int IgnoredRows, int ErrorRows, int WarningRows,
    int NewProducts, int ExistingProducts, int NewBatches, int ZeroQuantityProducts, IReadOnlyList<string> NewLocations);

public sealed record ImportReport(IReadOnlyList<ImportRowStatus> Statuses, IReadOnlyList<ImportPlanRow> Plan, ImportSummary Summary)
{
    public bool CanCommit => Summary.ErrorRows == 0 && Plan.Count > 0;
}

// وضعیت فعلی سیستم برای بررسی تکراری‌ها؛ کلیدها همه با ImportValidator ساخته می‌شوند.
public sealed class ImportContext
{
    // productKey ← RequiresExpiry
    public Dictionary<string, bool> ExistingProducts { get; } = [];
    // "{productKey}|{batchKey}"
    public HashSet<string> ExistingBatches { get; } = [];
    // barcodeKey ← productKey صاحب آن
    public Dictionary<string, string> ExistingBarcodes { get; } = [];
    // Key نام محل‌های ریشه (برای نام شیت)
    public HashSet<string> RootLocationKeys { get; } = [];
    // LocationKey مسیر کامل همه‌ی محل‌ها
    public HashSet<string> LocationPathKeys { get; } = [];
    // productKey قلم‌های غیرفعال؛ بچ تازه نمی‌گیرند (مثل ورود جنس).
    public HashSet<string> InactiveProducts { get; } = [];
    // LocationKey محل‌هایی که خودشان یا یکی از نیاکانشان غیرفعال است.
    public HashSet<string> InactiveLocationPathKeys { get; } = [];
}
