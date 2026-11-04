using System.Text;

namespace PharmacyWarehouse.Domain.Text;

public static class TextNormalizer
{
    private const char ZeroWidthNonJoiner = '‌';

    // ارقام فارسی (U+06F0 تا U+06F9) و عربی-هندی (U+0660 تا U+0669) را به ارقام لاتین تبدیل می‌کند.
    public static string ToAsciiDigits(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];

            if (c >= '۰' && c <= '۹')
            {
                chars[i] = (char)('0' + (c - '۰'));
            }
            else if (c >= '٠' && c <= '٩')
            {
                chars[i] = (char)('0' + (c - '٠'));
            }
        }

        return new string(chars);
    }

    // شکل نمایشی: قبل از ذخیره‌ی هر فیلد متنی. متن خالی null می‌شود.
    public static string? Clean(string? value)
    {
        if (value is null) return null;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;

        foreach (var original in ToAsciiDigits(value))
        {
            var c = original switch
            {
                'ي' => 'ی', // ي عربی ← ی فارسی
                'ك' => 'ک', // ك عربی ← ک فارسی
                _ => original
            };

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        var cleaned = builder.ToString().Trim(' ', ZeroWidthNonJoiner);
        return cleaned.Length == 0 ? null : cleaned;
    }

    // ستون‌های کلید و یکتایی: بدون هیچ فاصله و نیم‌فاصله، با حروف بزرگ.
    public static string? Key(string? value)
    {
        var cleaned = Clean(value);
        return cleaned?.Replace(" ", "").Replace(ZeroWidthNonJoiner.ToString(), "").ToUpperInvariant();
    }

    // SearchText و عبارت جست‌وجو: مثل Key ولی یک فاصله بین کلمه‌ها می‌ماند.
    public static string? SearchKey(string? value)
    {
        var cleaned = Clean(value);
        return cleaned?.Replace(ZeroWidthNonJoiner.ToString(), "").ToUpperInvariant();
    }
}
