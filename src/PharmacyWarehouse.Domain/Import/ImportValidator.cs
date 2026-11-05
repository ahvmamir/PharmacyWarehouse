using System.Globalization;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Import;

// قواعد خواندن ورود Excel (بخش ۱۲.۲ spec). سرور مرجع است: پیش‌نمایش و ثبت هر دو از همین تابع می‌گذرند.
public static class ImportValidator
{
    public const int MaxRows = 20000;
    private const string Placeholder = "*";

    public static string ProductKeyOf(string name, string? brand, string? pack) =>
        $"{TextNormalizer.Key(name)}\u001f{TextNormalizer.Key(brand) ?? ""}\u001f{TextNormalizer.Key(pack) ?? ""}";

    public static string BatchKeyOf(string productKey, string batchKey) => $"{productKey}|{batchKey}";

    // مسیر محل با جداکننده‌ی برنامه (›) یا / و > ؛ هر بخش جدا Key می‌شود.
    public static string? LocationKey(string? path)
    {
        if (TextNormalizer.Clean(path) is not { } cleaned) return null;
        var parts = cleaned.Split(['›', '/', '>'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(p => TextNormalizer.Key(p)).Where(p => p is not null);
        var key = string.Join(LocationTree.Separator.Trim(), parts);
        return key.Length == 0 ? null : key;
    }

    // «*» ابتدا و انتها (جای‌نگهدار قالب) برداشته می‌شود؛ خالی ← null.
    private static string? Strip(string? value)
    {
        var cleaned = TextNormalizer.Clean(value)?.Trim('*').Trim();
        return string.IsNullOrEmpty(cleaned) ? null : cleaned;
    }

    // عدد صحیح؛ «30.0» هم قبول است (Excel). null ← نامعتبر.
    private static int? ParseCount(string? raw)
    {
        var text = TextNormalizer.ToAsciiDigits(raw ?? "").Replace(",", "").Replace("٬", "").Trim();
        if (text.Length == 0) return null;
        if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d)) return null;
        if (d != decimal.Truncate(d) || d < 0 || d > int.MaxValue) return null;
        return (int)d;
    }

    private static string? NormalizePack(string? raw)
    {
        var cleaned = Strip(raw);
        if (cleaned is null) return null;
        // «30.0» از Excel ← «30»
        var ascii = TextNormalizer.ToAsciiDigits(cleaned);
        return decimal.TryParse(ascii, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == decimal.Truncate(d)
            ? ((long)d).ToString(CultureInfo.InvariantCulture)
            : cleaned;
    }

    public static ImportReport Validate(IReadOnlyList<ImportRowInput> rows, ImportContext context)
    {
        var statuses = new List<ImportRowStatus>(rows.Count);
        var plan = new List<ImportPlanRow>();

        // پیش‌گذر: کدام قلم‌ها «انقضا لازم نیست» دارند (علامت روی هر ردیف قلم، برای کل قلم).
        var noExpiry = new HashSet<string>();
        foreach (var r in rows)
        {
            if (Strip(r.Name) is { } n && r.ExpiryNotRequired) noExpiry.Add(ProductKeyOf(n, Strip(r.Brand), NormalizePack(r.UnitsPerPack)));
        }

        var batchesInFile = new HashSet<string>();
        var barcodeOwner = new Dictionary<string, string>();
        var productLocation = new Dictionary<string, string>();
        var newLocations = new List<string>();
        var newProducts = new HashSet<string>();
        var existingProducts = new HashSet<string>();
        var zeroProducts = new HashSet<string>();
        var batchesNew = 0;
        var ignored = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var messages = new List<string>();
            var errors = 0;
            var warnings = 0;
            void Error(string m) { messages.Add(m); errors++; }
            void Warn(string m) { messages.Add(m); warnings++; }

            var name = Strip(row.Name);
            if (name is null)
            {
                ignored++;
                statuses.Add(new ImportRowStatus(i, ImportSeverity.Ignored, []));
                continue;
            }

            if (name.Length > ProductRules.MaxName) Error($"نام حداکثر {ProductRules.MaxName} نویسه است.");
            var brand = Strip(row.Brand);
            if (brand is { Length: > ProductRules.MaxBrand }) Error($"برند حداکثر {ProductRules.MaxBrand} نویسه است.");
            var pack = NormalizePack(row.UnitsPerPack);
            if (pack is { Length: > ProductRules.MaxPack }) Error($"تعداد در بسته حداکثر {ProductRules.MaxPack} نویسه است.");
            var productKey = ProductKeyOf(name, brand, pack);

            var quantity = ParseCount(row.Quantity);
            if (quantity is null) Error("تعداد باید عدد صحیح (۰ یا بیشتر) باشد.");

            // محل: ستون «محل» (مسیر کامل) یا نام شیت
            string? locationPath = null;
            var locationText = Strip(row.Location);
            if (locationText is not null)
            {
                var key = LocationKey(locationText);
                var segments = locationText.Split(['›', '/', '>'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (key is null || segments.Any(seg => seg.Length > LocationTree.MaxName)) Error($"مسیر محل نامعتبر است (هر بخش حداکثر {LocationTree.MaxName} نویسه).");
                else
                {
                    locationPath = locationText;
                    // مسیر ناموجود ساخته می‌شود (مثل نام شیت)، تا خروجی برنامه در سیستم تازه هم قابل ورود باشد.
                    if (!context.LocationPathKeys.Contains(key) && !newLocations.Contains(locationText)) newLocations.Add(locationText);
                }
            }
            else if (TextNormalizer.Key(row.Sheet) is { } sheetKey && !context.RootLocationKeys.Contains(sheetKey))
            {
                var sheetName = TextNormalizer.Clean(row.Sheet)!;
                if (!newLocations.Contains(sheetName)) newLocations.Add(sheetName);
            }

            if (productLocation.TryGetValue(productKey, out var firstLocation))
            {
                var here = locationPath ?? TextNormalizer.Clean(row.Sheet) ?? "";
                if (!string.Equals(TextNormalizer.Key(firstLocation), TextNormalizer.Key(here), StringComparison.Ordinal))
                    Warn($"این قلم در «{here}» هم آمده؛ محل اولین ردیف («{firstLocation}») می‌ماند.");
            }
            else
            {
                productLocation[productKey] = locationPath ?? TextNormalizer.Clean(row.Sheet) ?? "";
            }

            var exists = context.ExistingProducts.TryGetValue(productKey, out var existingRequires);
            // قلم موجود: همان RequiresExpiry خودش (ورود Excel تعریف قلم موجود را عوض نمی‌کند)؛ قلم تازه: مگر «انقضا لازم نیست» روشن باشد.
            var requiresExpiry = exists ? existingRequires : !noExpiry.Contains(productKey);

            // بچ و تاریخ؛ در تعداد ۰ دور ریخته می‌شوند.
            string? batchNumber = null, batchKey = null;
            ExpiryValue? expiry = null;
            if (quantity == 0)
            {
                zeroProducts.Add(productKey);
                if (Strip(row.BatchNumber) is not null || Strip(row.Expiry) is not null)
                    messages.Add("تعداد ۰: فقط قلم تعریف می‌شود؛ بچ و تاریخ این ردیف نادیده گرفته شد.");
            }
            else
            {
                // تعداد نامعتبر هم بچ و تاریخ را می‌سنجد تا همه‌ی خطاهای ردیف یک‌جا دیده شوند.
                batchNumber = Strip(row.BatchNumber);
                batchKey = TextNormalizer.ScanKey(batchNumber);
                if (batchNumber is null || batchKey is null) Error("بچ نامبر لازم است.");
                else if (batchNumber.Length > ProductRules.MaxBatch) Error($"بچ نامبر حداکثر {ProductRules.MaxBatch} نویسه است.");
                else
                {
                    var full = BatchKeyOf(productKey, batchKey);
                    if (context.ExistingBatches.Contains(full)) Error("این بچ قبلاً در سیستم ثبت شده است؛ ورود Excel موجودی را جایگزین نمی‌کند.");
                    else if (!batchesInFile.Add(full)) Error("این بچ در همین فایل تکراری است.");
                }

                var expiryText = Strip(row.Expiry);
                var parsed = ExpiryDate.Parse(expiryText);
                if (parsed.Error is { } expiryError) Error(expiryError);
                else if (parsed.Value is { } value)
                {
                    expiry = value;
                    foreach (var w in parsed.Warnings) Warn(WarningText(w));
                }
                else if (requiresExpiry)
                {
                    Error("تاریخ انقضا لازم است؛ تاریخ بدهید یا «انقضا لازم نیست» را روشن کنید.");
                }
            }

            int? minStock = null;
            if (Strip(row.MinStock) is { } minText)
            {
                if (ParseCount(minText) is { } m) minStock = m;
                else Error("حداقل موجودی باید عدد صحیح باشد.");
            }

            long? unitCost = null;
            if (Strip(row.UnitCost) is { } costText)
            {
                var ascii = TextNormalizer.ToAsciiDigits(costText).Replace(",", "").Replace("٬", "");
                if (long.TryParse(ascii, NumberStyles.None, CultureInfo.InvariantCulture, out var cost)) unitCost = cost;
                else Error("قیمت باید عدد صحیح و نامنفی باشد.");
            }

            string? barcode = Strip(row.Barcode);
            var barcodeKey = TextNormalizer.ScanKey(barcode);
            if (barcodeKey is not null)
            {
                if (barcode!.Length > ProductRules.MaxCode) Error($"بارکد حداکثر {ProductRules.MaxCode} نویسه است.");
                else if (context.ExistingBarcodes.TryGetValue(barcodeKey, out var systemOwner) && systemOwner != productKey) Error("این بارکد برای قلم دیگری در سیستم ثبت شده است.");
                else if (barcodeOwner.TryGetValue(barcodeKey, out var owner) && owner != productKey) Error("این بارکد برای قلم دیگری در همین فایل آمده است.");
                else barcodeOwner[barcodeKey] = productKey;
            }

            var severity = errors > 0 ? ImportSeverity.Error : warnings > 0 ? ImportSeverity.Warning : ImportSeverity.Ok;
            statuses.Add(new ImportRowStatus(i, severity, messages));
            if (errors > 0 || quantity is null) continue;

            if (exists) existingProducts.Add(productKey); else newProducts.Add(productKey);
            if (quantity > 0) batchesNew++;
            plan.Add(new ImportPlanRow(i, TextNormalizer.Clean(row.Sheet) ?? "", locationPath, name, brand, pack, productKey, requiresExpiry,
                quantity.Value, quantity > 0 ? batchNumber : null, quantity > 0 ? batchKey : null, quantity > 0 ? expiry : null,
                barcode, barcodeKey, minStock, unitCost));
        }

        var summary = new ImportSummary(
            rows.Count, ignored, statuses.Count(s => s.Severity == ImportSeverity.Error), statuses.Count(s => s.Severity == ImportSeverity.Warning),
            newProducts.Count, existingProducts.Count, batchesNew, zeroProducts.Count, newLocations);
        return new ImportReport(statuses, plan, summary);
    }

    private static string WarningText(ExpiryWarning w) => w switch
    {
        ExpiryWarning.ConvertedFromJalali => "تاریخ شمسی به میلادی تبدیل شد.",
        ExpiryWarning.MonthOnly => "تاریخ فقط ماه داشت؛ آخر ماه ثبت می‌شود.",
        ExpiryWarning.DayClampedToMonthEnd => "اصلاح‌شده: روز از طول ماه بیشتر بود؛ آخر ماه ثبت می‌شود.",
        _ => ""
    };
}
