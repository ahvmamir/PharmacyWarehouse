namespace PharmacyWarehouse.Domain.Assets;

// برچسب فارسی فیلدها و مقدارهای enum، برای رابط کاربری و نمایش تاریخچه.
public static class AuditLabels
{
    private static readonly Dictionary<string, string> Fields = new()
    {
        ["AssetNumber"] = "شماره اموال",
        ["HasTemporaryAssetNumber"] = "شماره موقت",
        ["MouseModel"] = "مدل موس",
        ["KeyboardModel"] = "مدل کیبورد",
        ["Name"] = "نام",
        ["UserName"] = "نام کاربری",
        ["DisplayName"] = "نام نمایشی",
        ["Role"] = "نقش",
        ["Permissions"] = "مجوزها",
        ["PasswordReset"] = "ریست رمز",
        ["IsActive"] = "فعال",
        ["SortOrder"] = "ترتیب",
        ["SerialNumber"] = "سریال",
        ["Brand"] = "برند",
        ["Model"] = "مدل",
        ["SizeText"] = "سایز",
        ["ComputerName"] = "نام کامپیوتر",
        ["PeripheralKind"] = "نوع جانبی",
        ["HasMouse"] = "موس",
        ["HasKeyboard"] = "کیبورد",
        ["OtherAccessories"] = "جانبی‌های دیگر",
        ["OwnerUnitId"] = "واحد صاحب اموال",
        ["BuildingId"] = "ساختمان",
        ["FloorId"] = "طبقه",
        ["LocationUnitId"] = "واحد محل",
        ["Room"] = "اتاق",
        ["Station"] = "استیشن",
        ["StationPosition"] = "جای دقیق",
        ["Status"] = "وضعیت",
        ["ParentId"] = "متصل به",
        ["Notes"] = "یادداشت",
        ["Kind"] = "نوع قطعه",
        ["Capacity"] = "ظرفیت",
        ["Position"] = "ترتیب",
        ["LastVerifiedAtUtc"] = "تأیید حضور",
        ["SentAtUtc"] = "تاریخ ارسال",
        ["ReturnedAtUtc"] = "تاریخ برگشت",
        ["Vendor"] = "تعمیرگاه",
        ["Problem"] = "مشکل",
        ["Result"] = "نتیجه",
        ["CostRials"] = "هزینه (ریال)"
    };

    public static string Field(string? field) =>
        field is not null && Fields.TryGetValue(field, out var label) ? label : field ?? "";

    public static string Type(EquipmentType type) => type switch
    {
        EquipmentType.Case => "کیس",
        EquipmentType.Laptop => "لپ‌تاپ",
        EquipmentType.Monitor => "مانیتور",
        EquipmentType.Printer => "پرینتر",
        EquipmentType.Scanner => "اسکنر",
        EquipmentType.Peripheral => "جانبی",
        _ => type.ToString()
    };

    public static string Status(EquipmentStatus status) => status switch
    {
        EquipmentStatus.Active => "در حال استفاده",
        EquipmentStatus.InStorage => "در انبار",
        EquipmentStatus.InRepair => "در تعمیر",
        EquipmentStatus.Retired => "اسقاط",
        _ => status.ToString()
    };

    public static string Kind(ComponentKind kind) => kind switch
    {
        ComponentKind.Motherboard => "مادربورد",
        ComponentKind.Cpu => "CPU",
        ComponentKind.Ram => "RAM",
        ComponentKind.Gpu => "GPU",
        ComponentKind.PowerSupply => "پاور",
        ComponentKind.OpticalDrive => "DVD-RW",
        ComponentKind.Ssd => "SSD",
        ComponentKind.Hdd => "HDD",
        _ => kind.ToString()
    };

    // مقدار ذخیره‌شده در تاریخچه را خوانا می‌کند. شناسه‌ها به شکل id|name ذخیره می‌شوند
    // تا تغییر نام بعدی یک واحد، تاریخچه را گمراه نکند.
    public static string Value(string? field, string? value)
    {
        if (string.IsNullOrEmpty(value)) return "خالی";

        var separator = value.IndexOf('|');
        if (separator >= 0) return value[(separator + 1)..];

        if (bool.TryParse(value, out var flag)) return flag ? "بله" : "خیر";

        // تاریخ‌ها با قالب O ذخیره می‌شوند؛ نمایش شمسی.
        if (field?.EndsWith("AtUtc", StringComparison.Ordinal) == true
            && DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
        {
            return Time.JalaliDate.Format(at.ToUniversalTime());
        }

        return field switch
        {
            "Status" when Enum.TryParse<EquipmentStatus>(value, out var status) => Status(status),
            "Type" when Enum.TryParse<EquipmentType>(value, out var type) => Type(type),
            "Kind" when Enum.TryParse<ComponentKind>(value, out var kind) => Kind(kind),
            "CostRials" when long.TryParse(value, out var cost) => cost.ToString("N0", System.Globalization.CultureInfo.InvariantCulture),
            _ => value
        };
    }
}
