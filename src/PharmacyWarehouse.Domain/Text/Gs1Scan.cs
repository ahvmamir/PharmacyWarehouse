using System.Text;

namespace PharmacyWarehouse.Domain.Text;

// بارکد دوبعدی GS1 روی جعبه‌ی دارو (بخش ۹.۲ spec). نمونه‌ی جعبه‌های داروخانه (عکس امیر ۲۰۲۶-۱۱-۱۶):
// DataMatrix با GTIN (01)، UID سریال ۲۰ رقمی (21)، انقضا (17) و بچ (10).
// بارکدخوانِ صفحه‌کلیدی جداکننده‌ی GS (ASCII 29) را گاهی نمی‌فرستد؛ آن وقت مرز فیلدهای طول‌متغیر (10 و 21)
// معلوم نیست و همه‌ی برش‌های معتبر به‌عنوان نامزد برمی‌گردند تا صاحب صفحه با بچ‌های موجود انتخاب کند.
public sealed record Gs1Data(string Gtin, string? Batch, string? Serial, DateOnly? Expiry, bool ExpiryMonthOnly)
{
    // متن انقضا به قالبی که ExpiryDate.Parse می‌پذیرد: «روز/ماه/سال» یا «ماه/سال» وقتی روز در کد ۰۰ است.
    public string? ExpiryText => Expiry is not { } d ? null
        : ExpiryMonthOnly ? $"{d.Month:00}/{d.Year:0000}" : $"{d.Day:00}/{d.Month:00}/{d.Year:0000}";
}

public static class Gs1Scan
{
    public const char Gs = '\u001D';
    private const int MaxVariable = 20;
    private const int MaxCandidates = 16;

    // پیشوند شناسه‌ی نماد که بعضی بارکدخوان‌ها می‌فرستند (DataMatrix، GS1-128، QR).
    private static readonly string[] SymbologyPrefixes = ["]D2", "]C1", "]Q3", "]E0"];

    // شناسه‌های پشتیبانی‌شده: طول ثابت یا متغیر. شناسه‌ی ناشناخته یعنی «GS1 نیست».
    private static readonly Dictionary<string, int?> Ais = new()
    {
        ["01"] = 14, // GTIN
        ["11"] = 6,  // تاریخ تولید (خوانده و دور ریخته می‌شود)
        ["17"] = 6,  // انقضا YYMMDD
        ["10"] = null, // بچ
        ["21"] = null  // سریال (UID)
    };

    // بهترین برداشت: اگر چند برش ممکن است، آنکه سریالش ۲۰ رقم است (UID سامانه‌ی دارو) و بعد اولی.
    public static Gs1Data? Parse(string? raw)
    {
        var all = ParseAll(raw);
        return all.Count == 0 ? null : all.FirstOrDefault(c => c.Serial is { Length: 20 }) ?? all[0];
    }

    // همه‌ی برداشت‌های معتبر، بدون تکرار. خالی یعنی متن GS1 نیست (مثلاً بارکد معمولی EAN-13).
    public static IReadOnlyList<Gs1Data> ParseAll(string? raw)
    {
        if (Normalize(raw) is not { } text) return [];

        var results = new List<Gs1Data>();
        if (text.Contains('('))
        {
            if (ParseParenthesized(text) is { } p) results.Add(p);
            return results;
        }

        Walk(text, 0, new Dictionary<string, string>(), results);
        return results.Distinct().ToList();
    }

    // GTIN-14 با صفر اول همان EAN-13 است؛ بارکدهای ثبت‌شده هر کدام از این دو شکل را ممکن است داشته باشند.
    public static IReadOnlyList<string> CodeVariants(string gtinOrCode)
    {
        var list = new List<string> { gtinOrCode };
        if (gtinOrCode.Length == 14 && gtinOrCode[0] == '0' && gtinOrCode.All(char.IsAsciiDigit)) list.Add(gtinOrCode[1..]);
        if (gtinOrCode.Length == 13 && gtinOrCode.All(char.IsAsciiDigit)) list.Add("0" + gtinOrCode);
        return list;
    }

    // کلیدهای جست‌وجوی بارکد برای یک اسکن: GS1 ← GTIN و شکل ۱۳ رقمی‌اش؛ بارکد معمولی ← ScanKey و شکل دیگرش.
    public static IReadOnlyList<string> LookupKeys(string? raw)
    {
        if (Parse(raw) is { } data) return CodeVariants(data.Gtin);
        return TextNormalizer.ScanKey(raw) is { } key ? CodeVariants(key) : [];
    }

    // متنی که به‌عنوان بارکد ذخیره می‌شود: از کد GS1 فقط GTIN (UID هر جعبه فرق دارد و بارکد قلم نیست).
    public static string? StoredCode(string? raw) =>
        Parse(raw) is { } data ? data.Gtin : TextNormalizer.Clean(raw);

    // رقم کنترل GTIN (مُد ۱۰ با وزن ۳ و ۱ از راست).
    public static bool IsValidGtin(string gtin)
    {
        if (gtin.Length != 14 || !gtin.All(char.IsAsciiDigit)) return false;
        var sum = 0;
        for (var i = 0; i < 13; i++) sum += (gtin[12 - i] - '0') * (i % 2 == 0 ? 3 : 1);
        return (10 - sum % 10) % 10 == gtin[13] - '0';
    }

    // ارقام فارسی و حروف صفحه‌کلید فارسی ← لاتین (مثل ScanKey)، ولی جداکننده‌ی GS حفظ می‌شود.
    private static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (c is '\u001C' or '\u001D' or '\u001E' or '\u001F') { builder.Append(Gs); continue; }
            if (TextNormalizer.ScanKey(c.ToString()) is { } mapped) builder.Append(mapped);
        }

        var text = builder.ToString().Trim(Gs);
        foreach (var prefix in SymbologyPrefixes)
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal)) text = text[prefix.Length..];
        }

        // بدون GTIN در ابتدا GS1 حساب نمی‌شود؛ جلوی برداشت اشتباه از بارکدهای معمولی را می‌گیرد.
        return text.StartsWith("01", StringComparison.Ordinal) || text.StartsWith("(01)", StringComparison.Ordinal) ? text : null;
    }

    private static Gs1Data? ParseParenthesized(string text)
    {
        var fields = new Dictionary<string, string>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '(') return null;
            var close = text.IndexOf(')', i);
            if (close < 0) return null;
            var ai = text[(i + 1)..close];
            var next = text.IndexOf('(', close);
            var value = (next < 0 ? text[(close + 1)..] : text[(close + 1)..next]).Trim(Gs);
            if (!Ais.ContainsKey(ai) || !fields.TryAdd(ai, value)) return null;
            i = next < 0 ? text.Length : next;
        }

        return Build(fields);
    }

    private static void Walk(string text, int pos, Dictionary<string, string> fields, List<Gs1Data> results)
    {
        if (results.Count >= MaxCandidates) return;
        while (pos < text.Length && text[pos] == Gs) pos++;
        if (pos == text.Length)
        {
            if (Build(fields) is { } data) results.Add(data);
            return;
        }

        if (pos + 2 > text.Length) return;
        var ai = text.Substring(pos, 2);
        if (!Ais.TryGetValue(ai, out var length) || fields.ContainsKey(ai)) return;
        var start = pos + 2;

        if (length is { } fixedLength)
        {
            if (start + fixedLength > text.Length) return;
            var value = text.Substring(start, fixedLength);
            if (value.Contains(Gs)) return;
            fields[ai] = value;
            Walk(text, start + fixedLength, fields, results);
            fields.Remove(ai);
            return;
        }

        // طول‌متغیر: اگر GS پیش از سقف آمده، همان مرز است؛ وگرنه همه‌ی طول‌ها امتحان می‌شوند (بلندتر اول).
        var gs = text.IndexOf(Gs, start);
        if (gs >= 0 && gs - start <= MaxVariable)
        {
            if (gs == start) return;
            fields[ai] = text[start..gs];
            Walk(text, gs, fields, results);
            fields.Remove(ai);
            return;
        }

        var max = Math.Min(MaxVariable, text.Length - start);
        for (var len = max; len >= 1; len--)
        {
            var value = text.Substring(start, len);
            if (value.Contains(Gs)) continue;
            fields[ai] = value;
            Walk(text, start + len, fields, results);
            fields.Remove(ai);
        }
    }

    private static Gs1Data? Build(Dictionary<string, string> fields)
    {
        if (!fields.TryGetValue("01", out var gtin) || !IsValidGtin(gtin)) return null;

        DateOnly? expiry = null;
        var monthOnly = false;
        if (fields.TryGetValue("17", out var yymmdd))
        {
            if (!yymmdd.All(char.IsAsciiDigit)) return null;
            var year = 2000 + int.Parse(yymmdd[..2]);
            var month = int.Parse(yymmdd[2..4]);
            var day = int.Parse(yymmdd[4..]);
            if (month is < 1 or > 12) return null;
            // روز ۰۰ در GS1 یعنی «آخر ماه».
            monthOnly = day == 0;
            if (monthOnly) day = DateTime.DaysInMonth(year, month);
            if (day > DateTime.DaysInMonth(year, month)) return null;
            expiry = new DateOnly(year, month, day);
        }

        if (fields.TryGetValue("11", out var production) && !production.All(char.IsAsciiDigit)) return null;

        return new Gs1Data(gtin, fields.GetValueOrDefault("10"), fields.GetValueOrDefault("21"), expiry, monthOnly);
    }
}
