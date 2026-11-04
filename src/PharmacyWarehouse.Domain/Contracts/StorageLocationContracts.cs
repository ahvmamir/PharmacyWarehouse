namespace PharmacyWarehouse.Domain.Contracts;

// Path مسیر کامل از ریشه است («ضلع جنوبی › قفسه ۲»)؛ سرور حسابش می‌کند.
// IsUsable یعنی خود محل و همه‌ی نیاکانش فعال‌اند؛ انتخاب‌گرها فقط همین‌ها را پیشنهاد می‌دهند.
public sealed record StorageLocationItem(Guid Id, Guid? ParentId, string Name, string Path, int SortOrder, bool IsActive, int Version, bool IsUsable);

public sealed class SaveStorageLocationRequest
{
    public string Name { get; set; } = "";
    public Guid? ParentId { get; set; }
    public int SortOrder { get; set; }
    public int Version { get; set; }
}
