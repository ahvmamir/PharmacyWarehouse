using PharmacyWarehouse.Domain.Assets;

namespace PharmacyWarehouse.Domain.Contracts;

public static class ProblemTypes
{
    public const string DuplicateAssetNumber = "duplicate-asset-number";
    public const string VersionConflict = "version-conflict";
}

public static class BulkRules
{
    public const int MinCount = 2;
    public const int MaxCount = 200;
}

public sealed class BulkCreateRequest
{
    public List<EquipmentInput> Items { get; set; } = [];
}

public sealed class BulkCreateResponse
{
    public int Created { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class EquipmentSort
{
    public const string Device = "device";
    public const string AssetNumber = "asset";
    public const string ComputerName = "computer";
    public const string Location = "location";
    public const string Owner = "owner";
    public const string Status = "status";
    public const string Updated = "updated";
}

public sealed class UpdateEquipmentRequest : EquipmentInput
{
    public int Version { get; set; }
}

public sealed class MoveEquipmentRequest
{
    public Guid BuildingId { get; set; }
    public Guid FloorId { get; set; }
    public Guid LocationUnitId { get; set; }
    public string? Room { get; set; }
    public string? Station { get; set; }
    public string? StationPosition { get; set; }
    public bool MoveLinked { get; set; } = true;
    public string? Reason { get; set; }
    public int Version { get; set; }
}

public sealed class ChangeStatusRequest
{
    public EquipmentStatus Status { get; set; }
    public string? Reason { get; set; }
    public int Version { get; set; }
}

// کار گروهی روی فهرست: هر دستگاه با Version دیده‌شده؛ همه یا هیچ، در یک ChangeSet.
public sealed class BulkItem
{
    public Guid Id { get; set; }
    public int Version { get; set; }
}

public static class BulkActionRules
{
    public const int MaxItems = 500;
}

public sealed class BulkMoveRequest
{
    public List<BulkItem> Items { get; set; } = [];
    public Guid BuildingId { get; set; }
    public Guid FloorId { get; set; }
    public Guid LocationUnitId { get; set; }
    public string? Room { get; set; }
    public string? Station { get; set; }
    public string? StationPosition { get; set; }
    public bool MoveLinked { get; set; } = true;
    public string? Reason { get; set; }
}

public sealed class BulkStatusRequest
{
    public List<BulkItem> Items { get; set; } = [];
    public EquipmentStatus Status { get; set; }
    public string? Reason { get; set; }
}

public sealed class BulkActionResult
{
    public int Changed { get; init; }
}

// انبارگردانی: «دیدمش، همین‌جاست». فقط زمان تأیید ذخیره می‌شود؛ چه کسی، در تاریخچه است.
public sealed class VerifyRequest
{
    public int Version { get; set; }
}

public static class RepairRules
{
    public const int MaxVendor = 100;
    public const int MaxProblem = 500;
    public const int MaxResult = 500;
}

public sealed class RepairItem
{
    public Guid Id { get; init; }
    public DateTime SentAtUtc { get; init; }
    public string? Vendor { get; init; }
    public string Problem { get; init; } = "";
    public DateTime? ReturnedAtUtc { get; init; }
    public string? Result { get; init; }
    public long? CostRials { get; init; }
    public int Version { get; init; }
}

// ارسال به تعمیر؛ وضعیت دستگاه همان لحظه «در تعمیر» می‌شود (با Version دستگاه).
public sealed class RepairSendRequest
{
    public DateTime? SentAtUtc { get; set; }
    public string? Vendor { get; set; }
    public string? Problem { get; set; }
    public int EquipmentVersion { get; set; }
}

// برگشت از تعمیر؛ وضعیت دستگاه به وضعیت انتخاب‌شده (در حال استفاده یا در انبار) برمی‌گردد.
public sealed class RepairReturnRequest
{
    public DateTime? ReturnedAtUtc { get; set; }
    public string? Result { get; set; }
    public long? CostRials { get; set; }
    public EquipmentStatus NewStatus { get; set; } = EquipmentStatus.Active;
    public int Version { get; set; }
    public int EquipmentVersion { get; set; }
}

public sealed class LinkRequest
{
    public Guid ChildId { get; set; }
    public int Version { get; set; }
}

public sealed class EquipmentListItem
{
    public Guid Id { get; init; }
    public EquipmentType Type { get; init; }
    public EquipmentStatus Status { get; init; }
    public string? AssetNumber { get; init; }
    public bool HasTemporaryAssetNumber { get; init; }
    public string? SerialNumber { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? ComputerName { get; init; }
    public string? PeripheralKind { get; init; }
    public Guid? ParentId { get; init; }
    public string OwnerUnitName { get; init; } = "";
    public string BuildingName { get; init; } = "";
    public string FloorName { get; init; } = "";
    public string LocationUnitName { get; init; } = "";
    public string? Room { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? LastVerifiedAtUtc { get; init; }
    public Guid BuildingId { get; init; }
    public Guid FloorId { get; init; }
    public Guid LocationUnitId { get; init; }
    public int Version { get; init; }
}

public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public sealed class EquipmentStats
{
    public int Total { get; init; }
    public int Active { get; init; }
    public int InStorage { get; init; }
    public int InRepair { get; init; }
    public int Retired { get; init; }
    public int NoAssetNumber { get; init; }
}

public sealed class ComponentItem
{
    public Guid Id { get; init; }
    public ComponentKind Kind { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? Capacity { get; init; }
}

public sealed class EquipmentDetail
{
    public Guid Id { get; init; }
    public EquipmentType Type { get; init; }
    public EquipmentStatus Status { get; init; }
    public string? AssetNumber { get; init; }
    public bool HasTemporaryAssetNumber { get; init; }
    public string? SerialNumber { get; init; }
    public string? Brand { get; init; }
    public string? Model { get; init; }
    public string? SizeText { get; init; }
    public string? ComputerName { get; init; }
    public string? PeripheralKind { get; init; }
    public bool HasMouse { get; init; }
    public bool HasKeyboard { get; init; }
    public string? OtherAccessories { get; init; }
    public Guid OwnerUnitId { get; init; }
    public string OwnerUnitName { get; init; } = "";
    public Guid BuildingId { get; init; }
    public string BuildingName { get; init; } = "";
    public Guid FloorId { get; init; }
    public string FloorName { get; init; } = "";
    public Guid LocationUnitId { get; init; }
    public string LocationUnitName { get; init; } = "";
    public string? Room { get; init; }
    public string? Station { get; init; }
    public string? StationPosition { get; init; }
    public string? Notes { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public string? MouseModel { get; init; }
    public string? KeyboardModel { get; init; }
    public DateTime? LastVerifiedAtUtc { get; init; }
    public int Version { get; init; }
    public IReadOnlyList<ComponentItem> Components { get; init; } = [];
    public EquipmentListItem? Parent { get; init; }
    public IReadOnlyList<EquipmentListItem> Children { get; init; } = [];

    // فقط در پاسخ ثبت و ویرایش: نام کامپیوتر تکراری هشدار است، نه خطا.
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed class AuditEntryItem
{
    public Guid Id { get; init; }
    public Guid ChangeSetId { get; init; }
    public DateTime AtUtc { get; init; }
    public string UserName { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string? EntityLabel { get; init; }
    public string Action { get; init; } = "";
    public string? Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }

    // ردیفی که مال دستگاه دیگری است ولی به این دستگاه مربوط است (وصل یا جدا شدن آن)، مثل «مانیتور TEST-2».
    public string? Subject { get; init; }

    // متن خوانای مقدار، وقتی خود مقدار شناسه است (مثلاً «متصل به» ← «کیس TEST-1»).
    public string? OldText { get; init; }
    public string? NewText { get; init; }
}
