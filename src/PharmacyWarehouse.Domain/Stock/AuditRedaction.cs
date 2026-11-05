using System.Text.Encodings.Web;
using System.Text.Json;

namespace PharmacyWarehouse.Domain.Stock;

// حذف یک فیلد از JSON ردیف‌های «ثبت» و «حذف» تاریخچه (مثلاً قیمت واحد برای کسی که reports.view ندارد).
public static class AuditRedaction
{
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // JSON نامعتبر یا خالی دست‌نخورده برمی‌گردد (ردیف‌های قدیمی/غیر JSON را خراب نمی‌کنیم).
    public static string? WithoutKey(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        Dictionary<string, string?>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (values is null || !values.Remove(key)) return json;
        return JsonSerializer.Serialize(values, Readable);
    }

    // ردیف‌های قدیمی «ثبت» و «حذف» شناسه‌ی خام دارند و AuditEntries فقط اضافه‌شونده است؛ پس هنگام خواندن
    // شناسه به شکل «guid|نام» درمی‌آید. مقداری که از قبل نام دارد یا Guid نیست دست نمی‌خورد.
    public static string? WithResolvedId(string? json, string key, Func<Guid, string> nameOf)
    {
        if (string.IsNullOrWhiteSpace(json)) return json;

        Dictionary<string, string?>? values;
        try
        {
            values = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
        }
        catch (JsonException)
        {
            return json;
        }

        if (values is null || !values.TryGetValue(key, out var raw) || !Guid.TryParse(raw, out var id)) return json;

        values[key] = $"{id}|{nameOf(id)}";
        return JsonSerializer.Serialize(values, Readable);
    }
}
