using System.Globalization;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Assets;

// جدول بخش ۶ spec. سرور مرجع است؛ Client همین را برای پیام فوری صدا می‌زند.
public static class EquipmentRules
{
    public const int MaxAssetNumber = 50;
    public const int MaxSerialNumber = 100;
    public const int MaxBrand = 100;
    public const int MaxModel = 100;
    public const int MaxSizeText = 50;
    public const int MaxComputerName = 63;
    public const int MaxPeripheralKind = 100;
    public const int MaxOtherAccessories = 500;
    public const int MaxRoom = 100;
    public const int MaxStation = 100;
    public const int MaxStationPosition = 200;
    public const int MaxNotes = 2000;
    public const int MaxCapacity = 50;
    public const int MaxReason = 500;

    // مرکز مجموعه: قطعه، نام کامپیوتر و جانبی ارزان هم فقط همین‌ها را دارند.
    public static bool IsHub(EquipmentType type) => type is EquipmentType.Case or EquipmentType.Laptop;

    public const string TemporaryAssetPrefix = "TEMP-";

    // ظرفیت فقط برای قطعاتی که ظرفیت دارند.
    public static bool AllowsCapacity(ComponentKind kind) =>
        kind is ComponentKind.Ram or ComponentKind.Ssd or ComponentKind.Hdd;

    public static bool RequiresBrandAndModel(EquipmentType type) =>
        type is EquipmentType.Laptop or EquipmentType.Monitor or EquipmentType.Printer or EquipmentType.Scanner;

    public static bool ShowsSize(EquipmentType type) =>
        type is EquipmentType.Monitor or EquipmentType.Printer or EquipmentType.Scanner;

    // از RAM و SSD و HDD چند عدد مجاز است؛ از بقیه حداکثر یکی.
    public static bool AllowsMultiple(ComponentKind kind) =>
        kind is ComponentKind.Ram or ComponentKind.Ssd or ComponentKind.Hdd;

    public static IReadOnlyList<ValidationError> Validate(EquipmentInput input)
    {
        var errors = new List<ValidationError>();
        var type = input.Type;

        void Add(string field, string message) => errors.Add(new ValidationError(field, message));

        void Length(string field, string? value, int max, string label)
        {
            if (TextNormalizer.Clean(value)?.Length > max)
            {
                Add(field, $"{label} حداکثر {max} نویسه است.");
            }
        }

        static bool Filled(string? value) => TextNormalizer.Clean(value) is not null;

        if (!Enum.IsDefined(type))
        {
            Add(nameof(input.Type), "نوع دستگاه نامعتبر است.");
            return errors;
        }

        // شماره‌ی اموال
        if (input.UseTemporaryAssetNumber && Filled(input.AssetNumber))
        {
            Add(nameof(input.AssetNumber), "یا شماره اموال را وارد کنید یا «شماره اموال ندارد» را تیک بزنید، نه هر دو.");
        }
        else if (!input.UseTemporaryAssetNumber && !Filled(input.AssetNumber))
        {
            Add(nameof(input.AssetNumber), "شماره اموال را وارد کنید یا «شماره اموال ندارد» را تیک بزنید.");
        }

        // برند و مدل
        if (RequiresBrandAndModel(type))
        {
            if (!Filled(input.Brand)) Add(nameof(input.Brand), "برند را وارد کنید.");
            if (!Filled(input.Model)) Add(nameof(input.Model), "مدل را وارد کنید.");
        }

        // سایز
        if (type == EquipmentType.Monitor)
        {
            if (!Filled(input.SizeText))
            {
                Add(nameof(input.SizeText), "سایز مانیتور را وارد کنید.");
            }
            else if (!decimal.TryParse(
                         TextNormalizer.Clean(input.SizeText),
                         NumberStyles.AllowDecimalPoint,
                         CultureInfo.InvariantCulture,
                         out var inches)
                     || inches < 10 || inches > 100)
            {
                Add(nameof(input.SizeText), "سایز مانیتور باید عددی بین ۱۰ تا ۱۰۰ اینچ باشد.");
            }
        }
        else if (!ShowsSize(type) && Filled(input.SizeText))
        {
            Add(nameof(input.SizeText), "این نوع دستگاه سایز ندارد.");
        }

        // نوع جانبی
        if (type == EquipmentType.Peripheral)
        {
            if (!Filled(input.PeripheralKind)) Add(nameof(input.PeripheralKind), "نوع جانبی را وارد کنید.");
        }
        else if (Filled(input.PeripheralKind))
        {
            Add(nameof(input.PeripheralKind), "نوع جانبی فقط برای تجهیزات جانبی است.");
        }

        // مخصوص کیس و لپ‌تاپ
        if (!IsHub(type))
        {
            if (Filled(input.ComputerName)) Add(nameof(input.ComputerName), "نام کامپیوتر فقط برای کیس و لپ‌تاپ است.");
            if (input.Components.Count > 0) Add(nameof(input.Components), "قطعه فقط برای کیس و لپ‌تاپ است.");
            if (input.HasMouse || input.HasKeyboard || Filled(input.MouseModel) || Filled(input.KeyboardModel)
                || Filled(input.OtherAccessories))
            {
                Add(nameof(input.OtherAccessories), "جانبی‌ها فقط برای کیس و لپ‌تاپ است.");
            }

            if (input.LinkChildIds.Count > 0) Add("Link", "فقط کیس و لپ‌تاپ دستگاه متصل دارند.");
        }
        else if (input.LinkParentId is not null)
        {
            Add("Link", "کیس و لپ‌تاپ را نمی‌شود به دستگاه دیگری وصل کرد.");
        }

        if (!input.HasMouse && Filled(input.MouseModel)) Add(nameof(input.MouseModel), "اول تیک موس را بزنید.");
        if (!input.HasKeyboard && Filled(input.KeyboardModel)) Add(nameof(input.KeyboardModel), "اول تیک کیبورد را بزنید.");

        // قطعات
        foreach (var group in input.Components.GroupBy(c => c.Kind))
        {
            if (!Enum.IsDefined(group.Key))
            {
                Add(nameof(input.Components), "نوع قطعه نامعتبر است.");
            }
            else if (!AllowsMultiple(group.Key) && group.Count() > 1)
            {
                Add(nameof(input.Components), $"از {AuditLabels.Kind(group.Key)} فقط یک عدد مجاز است.");
            }
        }

        foreach (var component in input.Components)
        {
            var label = Enum.IsDefined(component.Kind) ? AuditLabels.Kind(component.Kind) : "قطعه";
            Length(nameof(input.Components), component.Brand, MaxBrand, $"برند {label}");
            Length(nameof(input.Components), component.Model, MaxModel, $"مدل {label}");
            Length(nameof(input.Components), component.Capacity, MaxCapacity, $"ظرفیت {label}");

            if (Enum.IsDefined(component.Kind) && !AllowsCapacity(component.Kind) && Filled(component.Capacity))
            {
                Add(nameof(input.Components), $"{label} ظرفیت ندارد.");
            }
        }

        // مالکیت و محل
        if (input.OwnerUnitId == Guid.Empty) Add(nameof(input.OwnerUnitId), "واحد صاحب اموال را انتخاب کنید.");
        if (input.BuildingId == Guid.Empty) Add(nameof(input.BuildingId), "ساختمان را انتخاب کنید.");
        if (input.FloorId == Guid.Empty) Add(nameof(input.FloorId), "طبقه را انتخاب کنید.");
        if (input.LocationUnitId == Guid.Empty) Add(nameof(input.LocationUnitId), "واحد محل را انتخاب کنید.");

        // طول فیلدها
        Length(nameof(input.AssetNumber), input.AssetNumber, MaxAssetNumber, "شماره اموال");
        Length(nameof(input.SerialNumber), input.SerialNumber, MaxSerialNumber, "سریال");
        Length(nameof(input.Brand), input.Brand, MaxBrand, "برند");
        Length(nameof(input.Model), input.Model, MaxModel, "مدل");
        Length(nameof(input.SizeText), input.SizeText, MaxSizeText, "سایز");
        Length(nameof(input.ComputerName), input.ComputerName, MaxComputerName, "نام کامپیوتر");
        Length(nameof(input.PeripheralKind), input.PeripheralKind, MaxPeripheralKind, "نوع جانبی");
        Length(nameof(input.MouseModel), input.MouseModel, MaxModel, "مدل موس");
        Length(nameof(input.KeyboardModel), input.KeyboardModel, MaxModel, "مدل کیبورد");
        Length(nameof(input.OtherAccessories), input.OtherAccessories, MaxOtherAccessories, "جانبی‌های دیگر");
        Length(nameof(input.Room), input.Room, MaxRoom, "اتاق");
        Length(nameof(input.Station), input.Station, MaxStation, "استیشن");
        Length(nameof(input.StationPosition), input.StationPosition, MaxStationPosition, "جای دقیق");
        Length(nameof(input.Notes), input.Notes, MaxNotes, "یادداشت");

        return errors;
    }
}
