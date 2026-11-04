namespace PharmacyWarehouse.Domain.Assets;

// ورودی ثبت و ویرایش دستگاه؛ هم بدنه‌ی درخواست است و هم ورودی EquipmentRules.
public class EquipmentInput
{
    public EquipmentType Type { get; set; }
    public string? AssetNumber { get; set; }

    // شماره اموال ندارد: سرور شماره‌ی موقت می‌سازد.
    public bool UseTemporaryAssetNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SizeText { get; set; }
    public string? ComputerName { get; set; }
    public string? PeripheralKind { get; set; }
    public bool HasMouse { get; set; }
    public bool HasKeyboard { get; set; }
    public string? MouseModel { get; set; }
    public string? KeyboardModel { get; set; }
    public string? OtherAccessories { get; set; }
    public Guid OwnerUnitId { get; set; }
    public Guid BuildingId { get; set; }
    public Guid FloorId { get; set; }
    public Guid LocationUnitId { get; set; }
    public string? Room { get; set; }
    public string? Station { get; set; }
    public string? StationPosition { get; set; }
    public string? Notes { get; set; }
    public List<ComponentInput> Components { get; set; } = [];

    // فقط هنگام ثبت: کیس ← دستگاه‌های متصل؛ بقیه ← مرکز.
    public List<Guid> LinkChildIds { get; set; } = [];
    public Guid? LinkParentId { get; set; }
    public bool MoveLinkedToSameLocation { get; set; } = true;

    // برای ثبت چندتایی. فرزندها کپی نمی‌شوند، چون هر دستگاه فقط به یک مرکز وصل می‌شود.
    public EquipmentInput Copy() => new()
    {
        Type = Type,
        AssetNumber = AssetNumber,
        UseTemporaryAssetNumber = UseTemporaryAssetNumber,
        SerialNumber = SerialNumber,
        Brand = Brand,
        Model = Model,
        SizeText = SizeText,
        ComputerName = ComputerName,
        PeripheralKind = PeripheralKind,
        HasMouse = HasMouse,
        HasKeyboard = HasKeyboard,
        MouseModel = MouseModel,
        KeyboardModel = KeyboardModel,
        OtherAccessories = OtherAccessories,
        OwnerUnitId = OwnerUnitId,
        BuildingId = BuildingId,
        FloorId = FloorId,
        LocationUnitId = LocationUnitId,
        Room = Room,
        Station = Station,
        StationPosition = StationPosition,
        Notes = Notes,
        LinkParentId = LinkParentId,
        Components = Components
            .Select(c => new ComponentInput { Kind = c.Kind, Brand = c.Brand, Model = c.Model, Capacity = c.Capacity })
            .ToList()
    };
}

public sealed class ComponentInput
{
    // خالی یعنی قطعه‌ی جدید؛ پر یعنی همان قطعه‌ی موجود (برای تاریخچه‌ی درست).
    public Guid? Id { get; set; }
    public ComponentKind Kind { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Capacity { get; set; }
}

public sealed record ValidationError(string Field, string Message);
