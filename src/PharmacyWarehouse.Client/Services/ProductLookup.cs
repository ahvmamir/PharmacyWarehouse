using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public enum LookupKind { Found, NotFound, Error }

// Product: فقط وقتی Found است پر است. Error: فقط وقتی Error است.
public sealed record LookupResult(LookupKind Kind, ProductDetail? Product = null, string? Error = null);

// منطق مشترک اسکن صفحه‌های اقلام و ورود جنس: همیشه اول بارکد، بعد بازگشت به جست‌وجوی نام.
// ترتیب مهم است: اسکن با چیدمان فارسی کیبورد («فثسف0000000001») حرف فارسی دارد ولی بارکد است،
// پس نباید پیش از جست‌وجوی بارکد به‌خاطر ظاهرش «نام» حساب شود.
public static class ProductLookup
{
    // اسم‌گونه: حرف فارسی یا فاصله دارد. فقط برای تصمیم پس از شکست جست‌وجوی بارکد (پیام یا جست‌وجوی نام).
    public static bool LooksLikeName(string text) =>
        text.Any(c => c == ' ' || (c >= '؀' && c <= 'ۿ' && char.IsLetter(c)));

    public static async Task<LookupResult> ByCodeAsync(ProductsClient api, string text)
    {
        var result = await api.ByCodeAsync(text);
        if (result.IsSuccess) return new LookupResult(LookupKind.Found, result.Value);
        return result.Outcome == ApiOutcome.NotFound
            ? new LookupResult(LookupKind.NotFound)
            : new LookupResult(LookupKind.Error, Error: result.ErrorMessage);
    }
}
