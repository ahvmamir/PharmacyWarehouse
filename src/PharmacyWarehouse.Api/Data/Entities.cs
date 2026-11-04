using PharmacyWarehouse.Domain.Assets;

namespace PharmacyWarehouse.Api.Data;

// هر موجودیتی که تغییرش باید در تاریخچه بیاید. Version توکن هم‌زمانی است و رهگیر آن را زیاد می‌کند.
public interface IAudited
{
    Guid Id { get; }
    int Version { get; set; }
}

public class Building : IAudited
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public class Floor : IAudited
{
    public Guid Id { get; set; }
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public class Unit : IAudited
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

public class Equipment : IAudited
{
    public Guid Id { get; set; }
    public EquipmentType Type { get; set; }
    public string? AssetNumber { get; set; }
    public string? AssetNumberKey { get; set; }
    public bool HasTemporaryAssetNumber { get; set; }
    public string? SerialNumber { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? SizeText { get; set; }
    public string? ComputerName { get; set; }
    public string? ComputerNameKey { get; set; }
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
    public EquipmentStatus Status { get; set; } = EquipmentStatus.Active;
    public Guid? ParentId { get; set; }
    public string? Notes { get; set; }
    public string SearchText { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastVerifiedAtUtc { get; set; }
    public int Version { get; set; } = 1;

    public Unit? OwnerUnit { get; set; }
    public Building? Building { get; set; }
    public Floor? Floor { get; set; }
    public Unit? LocationUnit { get; set; }
    public Equipment? Parent { get; set; }
    public List<Equipment> Children { get; set; } = [];
    public List<Component> Components { get; set; } = [];
}

// یک بار رفتن به تعمیر. ردیف‌ها حذف نمی‌شوند؛ دستگاهِ دارای سابقه‌ی تعمیر هم حذف نمی‌شود.
public class RepairRecord : IAudited
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public DateTime SentAtUtc { get; set; }
    public string? Vendor { get; set; }
    public string Problem { get; set; } = "";
    public DateTime? ReturnedAtUtc { get; set; }
    public string? Result { get; set; }
    public long? CostRials { get; set; }
    public int Version { get; set; } = 1;
}

public class Component : IAudited
{
    public Guid Id { get; set; }
    public Guid EquipmentId { get; set; }
    public ComponentKind Kind { get; set; }
    public string? Brand { get; set; }
    public string? Model { get; set; }
    public string? Capacity { get; set; }
    public int Position { get; set; }
    public int Version { get; set; } = 1;
}

// فقط اضافه‌شونده؛ trigger جلوی UPDATE و DELETE را می‌گیرد.
public class AuditEntry
{
    public Guid Id { get; set; }
    public Guid ChangeSetId { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public Guid? EquipmentId { get; set; }

    // برای قطعه نوع آن (مثلاً Ram)، تا «ظرفیت از ۴ به ۸» بگوید کدام قطعه.
    public string? EntityLabel { get; set; }
    public string Action { get; set; } = "";
    public string? Field { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Reason { get; set; }
}

public static class AuditActions
{
    public const string Created = "Created";
    public const string Updated = "Updated";
    public const string Deleted = "Deleted";
}
