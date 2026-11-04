using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// جست‌وجو و فیلترهای فهرست را نگه می‌دارد تا برگشتن از صفحه‌ی یک دستگاه، کار کاربر را صفر نکند.
public sealed class EquipmentListState
{
    public EquipmentQuery Query { get; } = new();
    public bool ShowFilters { get; set; }
}

public static class EquipmentText
{
    public static string Title(EquipmentType type, string? brand, string? model, string? peripheralKind)
    {
        var name = string.Join(' ', new[] { brand, model }.Where(v => !string.IsNullOrEmpty(v)));
        if (name.Length > 0) return name;
        return peripheralKind ?? AuditLabels.Type(type);
    }

    public static string Title(EquipmentListItem item) => Title(item.Type, item.Brand, item.Model, item.PeripheralKind);

    public static string Title(EquipmentDetail item) => Title(item.Type, item.Brand, item.Model, item.PeripheralKind);

    // ویرگول، نه نقطه‌ی میانی: «·» در متن فارسی با رقم صفر (۰) اشتباه می‌شود.
    public static string Place(params string?[] parts) =>
        string.Join("، ", parts.Where(p => !string.IsNullOrEmpty(p)));
}
