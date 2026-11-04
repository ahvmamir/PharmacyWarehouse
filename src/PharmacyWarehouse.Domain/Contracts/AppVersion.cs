using System.Reflection;

namespace PharmacyWarehouse.Domain.Contracts;

public static class AppVersion
{
    // نسخه‌ی کامل اسمبلی؛ SDK در مخزن git شناسه‌ی کامیت را بعد از «+» اضافه می‌کند.
    public static string? Of(Assembly assembly) =>
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

    // «1.3.0+abc1234def…» ← «1.3.0 (abc1234)»؛ برای نمایش.
    public static string Display(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "نامعلوم";

        var plus = informational.IndexOf('+');
        if (plus < 0) return informational;

        var commit = informational[(plus + 1)..];
        return $"{informational[..plus]} ({commit[..Math.Min(7, commit.Length)]})";
    }

    // کلاینت و سرور از یک ساخت‌اند یا نه؛ نسخه‌ی کهنه‌ی کلاینت در کش مرورگر یعنی باید صفحه تازه شود.
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
}
