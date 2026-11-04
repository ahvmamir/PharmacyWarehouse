namespace PharmacyWarehouse.Domain.Contracts;

public static class LocationRules
{
    public const int MaxBuildingName = 100;
    public const int MaxFloorName = 50;
    public const int MaxUnitName = 100;
}

public sealed record BuildingItem(Guid Id, string Name, bool IsActive, int Version);

public sealed record FloorItem(Guid Id, Guid BuildingId, string Name, int SortOrder, bool IsActive, int Version);

public sealed record UnitItem(Guid Id, string Name, bool IsActive, int Version);

public sealed class SaveBuildingRequest
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
}

public sealed class SaveFloorRequest
{
    public Guid BuildingId { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public int Version { get; set; }
}

public sealed class SaveUnitRequest
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
}

// بدنه‌ی فعال‌سازی و غیرفعال‌سازی ساختمان، طبقه و واحد.
public sealed class VersionRequest
{
    public int Version { get; set; }
}
