namespace PharmacyWarehouse.Domain.Stock;

// قاعده‌های دسته‌ی قلم (بخش ۱۵.۳ spec). نام بعد از TextNormalizer.Clean سنجیده می‌شود.
public static class CategoryRules
{
    public const int MaxName = 60;

    public static string? NameError(string? cleanName) =>
        cleanName is null ? "نام دسته را وارد کنید."
        : cleanName.Length > MaxName ? $"نام دسته حداکثر {MaxName} نویسه است."
        : null;
}
