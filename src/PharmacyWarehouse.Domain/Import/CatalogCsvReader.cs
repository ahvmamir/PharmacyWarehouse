using System.Globalization;
using System.Text;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Import;

// یک ردیف معتبر فهرست مرجع (بخش ۱۵.۴ spec). همه‌ی متن‌ها از TextNormalizer.Clean گذشته‌اند؛ Gtin همیشه ۱۴ رقم با رقم کنترل درست.
public sealed record CatalogRow(
    string? Irc, string Gtin, string? GenericCode, string? GenericName, string? BrandName, string? PersianBrandName,
    string? LicenseOwner, string? Producer, string? ProducerCountry, int? PackageCount, long? ConsumerPrice, bool IsTemporaryIrc);

// MissingColumn پر یعنی سرستون اجباری نیست و Rows خالی است. DuplicateGtin: ردیف‌هایی که GTIN آن‌ها پیش‌تر در همین فایل آمده (درج می‌شوند، فقط شمرده می‌شوند).
public sealed record CatalogReadResult(IReadOnlyList<CatalogRow> Rows, int Empty, int Invalid, int DuplicateGtin, string? MissingColumn);

// خواننده‌ی CSV خروجی TTAC بدون کتابخانه‌ی خارجی: UTF-8 با یا بی BOM، نقل‌قول با کاما و خط تازه و "" داخل آن،
// ستون‌ها با نام سرستون (بی‌حساسیت به حروف بزرگ و کوچک)، نه ترتیب. ردیف تماماً خالی نادیده گرفته و شمرده می‌شود.
public static class CatalogCsvReader
{
    public const int MaxText = 300;

    public static readonly string[] RequiredColumns = ["Irc", "GTIN", "BrandName"];

    public static CatalogReadResult Read(Stream stream)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        return Read(reader.ReadToEnd());
    }

    public static CatalogReadResult Read(string text)
    {
        var records = ParseCsv(text);
        if (records.Count == 0) return new CatalogReadResult([], 0, 0, 0, RequiredColumns[0]);

        var header = records[0].Select(h => h.Trim().Trim('﻿')).ToList();
        int Index(string name) => header.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        foreach (var required in RequiredColumns)
        {
            if (Index(required) < 0) return new CatalogReadResult([], 0, 0, 0, required);
        }

        var columns = new[]
        {
            "Irc", "GTIN", "GenericCode", "DrugGenericName", "BrandName", "PersianBrandName", "LicenseOwnerCompanyName",
            "ProducerCompanyName", "ProducerCountryName", "PackageCount", "ConsumerPrice", "IsTemporaryIRC"
        }.ToDictionary(c => c, Index);

        var rows = new List<CatalogRow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int empty = 0, invalid = 0, duplicate = 0;
        for (var r = 1; r < records.Count; r++)
        {
            var record = records[r];
            if (record.All(string.IsNullOrWhiteSpace)) { empty++; continue; }

            string? Cell(string column) => columns[column] is var i && i >= 0 && i < record.Count ? Text(record[i]) : null;

            if (NormalizeGtin(Cell("GTIN")) is not { } gtin) { invalid++; continue; }
            if (!seen.Add(gtin)) duplicate++;

            rows.Add(new CatalogRow(
                Cell("Irc"), gtin, Cell("GenericCode"), Cell("DrugGenericName"), Cell("BrandName"), Cell("PersianBrandName"),
                Cell("LicenseOwnerCompanyName"), Cell("ProducerCompanyName"), Cell("ProducerCountryName"),
                int.TryParse(Cell("PackageCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pack) && pack > 0 ? pack : null,
                long.TryParse(Cell("ConsumerPrice"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var price) && price >= 0 ? price : null,
                string.Equals(Cell("IsTemporaryIRC"), "TRUE", StringComparison.OrdinalIgnoreCase)));
        }

        return new CatalogReadResult(rows, empty, invalid, duplicate, null);
    }

    // GTIN-8 تا GTIN-14 با صفر آغازین به ۱۴ رقم می‌رسد (قاعده‌ی GS1؛ رقم کنترل عوض نمی‌شود). ارقام فارسی پذیرفته می‌شوند.
    public static string? NormalizeGtin(string? raw)
    {
        var key = TextNormalizer.ScanKey(raw);
        if (key is null || key.Length is < 8 or > 14 || !key.All(char.IsAsciiDigit)) return null;
        var gtin = key.PadLeft(14, '0');
        return Gs1Scan.IsValidGtin(gtin) ? gtin : null;
    }

    private static string? Text(string raw)
    {
        var cleaned = TextNormalizer.Clean(raw);
        return cleaned is { Length: > MaxText } ? cleaned[..MaxText] : cleaned;
    }

    // RFC 4180: کاما جداکننده، "..." برای فیلد دارای کاما یا خط تازه، "" یعنی یک ". CRLF و LF هر دو پایان ردیف‌اند.
    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}

// نگاشت ردیف مرجع به فیلدهای تعریف قلم [تصمیم ۲۰۲۶-۱۱-۱۷، بخش ۱۵.۴ spec].
public static class CatalogMapping
{
    public static string Name(string? persianBrandName, string? brandName) => persianBrandName ?? brandName ?? "";

    public static string? Pack(int? packageCount) => packageCount is { } n ? $"{n} عدد" : null;
}
