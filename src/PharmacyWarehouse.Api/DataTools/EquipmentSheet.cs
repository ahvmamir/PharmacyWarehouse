using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.DataTools;

// ستون‌های مشترک گزارش فهرست، بکاپ Excel و ورود. همین نام‌ها را ورود می‌خواند،
// پس خروجی همین برنامه را می‌شود مستقیم دوباره وارد کرد.
public static class EquipmentSheet
{
    public const string Type = "نوع";
    public const string AssetNumber = "شماره اموال";
    public const string Serial = "سریال";
    public const string Brand = "برند";
    public const string Model = "مدل";
    public const string Size = "سایز";
    public const string ComputerName = "نام کامپیوتر";
    public const string PeripheralKind = "نوع جانبی";
    public const string Mouse = "موس";
    public const string MouseModel = "مدل موس";
    public const string Keyboard = "کیبورد";
    public const string KeyboardModel = "مدل کیبورد";
    public const string OtherAccessories = "جانبی‌های دیگر";
    public const string Owner = "واحد صاحب اموال";
    public const string Building = "ساختمان";
    public const string Floor = "طبقه";
    public const string LocationUnit = "واحد محل";
    public const string Room = "اتاق";
    public const string Station = "استیشن";
    public const string StationPosition = "جای دقیق";
    public const string Status = "وضعیت";
    public const string Notes = "یادداشت";
    public const string Parent = "متصل به";
    public const string UpdatedAt = "آخرین تغییر";

    public static readonly string[] Columns =
    [
        Type, AssetNumber, Serial, Brand, Model, Size, ComputerName, PeripheralKind, Mouse, MouseModel, Keyboard,
        KeyboardModel, OtherAccessories, Owner, Building, Floor, LocationUnit, Room, Station, StationPosition, Status,
        Notes, Parent, UpdatedAt
    ];

    // ستون‌هایی که برای این نوع‌ها معنی دارند؛ قالب Excel و جدول اصلاح در مرورگر همین‌ها را دارند.
    // یک نوع یعنی ستون «نوع» لازم نیست. خالی یعنی همه‌ی نوع‌ها.
    public static string[] ColumnsFor(IReadOnlyCollection<EquipmentType> types)
    {
        var all = types.Count == 0 ? Enum.GetValues<EquipmentType>() : types.ToArray();
        var columns = new List<string>();

        if (all.Length != 1) columns.Add(Type);
        columns.AddRange([AssetNumber, Serial, Brand, Model]);
        if (all.Any(EquipmentRules.ShowsSize)) columns.Add(Size);
        if (all.Any(EquipmentRules.IsHub)) columns.Add(ComputerName);
        if (all.Contains(EquipmentType.Peripheral)) columns.Add(PeripheralKind);
        if (all.Any(EquipmentRules.IsHub)) columns.AddRange([Mouse, MouseModel, Keyboard, KeyboardModel, OtherAccessories]);
        columns.AddRange([Owner, Building, Floor, LocationUnit, Room, Station, StationPosition, Status, Notes]);

        return columns.ToArray();
    }

    public static async Task<List<string?[]>> RowsAsync(IQueryable<Equipment> query)
    {
        var devices = await query.AsNoTracking()
            .OrderBy(e => e.AssetNumberKey)
            .Select(e => new
            {
                e.Type, e.AssetNumber, e.SerialNumber, e.Brand, e.Model, e.SizeText, e.ComputerName, e.PeripheralKind,
                e.HasMouse, e.MouseModel, e.HasKeyboard, e.KeyboardModel, e.OtherAccessories,
                Owner = e.OwnerUnit!.Name, Building = e.Building!.Name, Floor = e.Floor!.Name,
                LocationUnit = e.LocationUnit!.Name, e.Room, e.Station, e.StationPosition, e.Status, e.Notes,
                Parent = e.Parent != null ? e.Parent.AssetNumber : null, e.UpdatedAtUtc
            })
            .ToListAsync();

        return devices.Select(e => new[]
        {
            AuditLabels.Type(e.Type), e.AssetNumber, e.SerialNumber, e.Brand, e.Model, e.SizeText, e.ComputerName,
            e.PeripheralKind, e.HasMouse ? "بله" : null, e.MouseModel, e.HasKeyboard ? "بله" : null, e.KeyboardModel,
            e.OtherAccessories, e.Owner, e.Building, e.Floor, e.LocationUnit, e.Room, e.Station, e.StationPosition,
            AuditLabels.Status(e.Status), e.Notes, e.Parent, JalaliDate.Format(e.UpdatedAtUtc)
        }).ToList();
    }
}
