namespace PharmacyWarehouse.Domain.Contracts;

// نوع problem در پاسخ‌های ۴۰۹، تا کلاینت ناسازگاری Version را از بقیه جدا کند.
public static class ProblemTypes
{
    public const string VersionConflict = "version-conflict";
}
