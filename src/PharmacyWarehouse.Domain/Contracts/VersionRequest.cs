namespace PharmacyWarehouse.Domain.Contracts;

// بدنه‌ی فعال‌سازی و غیرفعال‌سازی: فقط Version دیده‌شده.
public sealed class VersionRequest
{
    public int Version { get; set; }
}
