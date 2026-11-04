namespace PharmacyWarehouse.Domain.Labels;

// بارکد Code 128 برای برچسب اموال، بدون کتابخانه‌ی خارجی.
// بارکدخوان‌های USB معمولی آن را مثل تایپ کیبورد می‌خوانند، پس کادر جست‌وجو مستقیم کار می‌کند.
// شماره‌ی تمام‌رقمی با طول زوج در Code C (دو رقم در هر نماد، بارکد کوتاه‌تر)، بقیه در Code B.
public static class Code128
{
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;

    // پهنای میله و فاصله‌ی هر نماد (۶ عضو، جمع ۱۱ واحد؛ Stop هفت عضو و ۱۳ واحد). جدول استاندارد ISO/IEC 15417.
    public static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    // رقم فارسی و عربی به رقم لاتین؛ نویسه‌ی دیگری که در Code B نیست، یعنی بارکد ساخته نمی‌شود.
    public static string? ToEncodable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var chars = text.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (c is >= '۰' and <= '۹') chars[i] = (char)('0' + (c - '۰'));
            else if (c is >= '٠' and <= '٩') chars[i] = (char)('0' + (c - '٠'));
            else if (c is < ' ' or > '~') return null;
        }

        return new string(chars);
    }

    // مقدار نمادها، شامل Start، چک‌سام و Stop. null یعنی متن قابل کدگذاری نیست.
    public static IReadOnlyList<int>? Encode(string? text)
    {
        var value = ToEncodable(text);
        if (value is null) return null;

        var symbols = new List<int>();
        if (value.Length >= 2 && value.Length % 2 == 0 && value.All(char.IsAsciiDigit))
        {
            symbols.Add(StartC);
            for (var i = 0; i < value.Length; i += 2)
            {
                symbols.Add((value[i] - '0') * 10 + (value[i + 1] - '0'));
            }
        }
        else
        {
            symbols.Add(StartB);
            symbols.AddRange(value.Select(c => c - ' '));
        }

        var checksum = symbols[0];
        for (var i = 1; i < symbols.Count; i++)
        {
            checksum += symbols[i] * i;
        }

        symbols.Add(checksum % 103);
        symbols.Add(Stop);
        return symbols;
    }

    // پهنای عناصر به ترتیب: میله، فاصله، میله، ... (بدون حاشیه‌ی سفید دو طرف).
    public static IReadOnlyList<int>? Widths(string? text)
    {
        var symbols = Encode(text);
        if (symbols is null) return null;

        return symbols.SelectMany(s => Patterns[s].Select(c => c - '0')).ToList();
    }
}
