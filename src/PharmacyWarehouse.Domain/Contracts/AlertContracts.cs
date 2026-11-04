namespace PharmacyWarehouse.Domain.Contracts;

// اعلان‌ها از وضعیت فعلی داده و سرور ساخته می‌شوند، نه از جدول؛ چیزی برای «خوانده‌شده» ذخیره نمی‌شود
// و اعلان با رفع مشکل خودش می‌رود.
public static class AlertSeverity
{
    public const string Danger = "danger";
    public const string Warning = "warning";
    public const string Info = "info";
}

public static class AlertKeys
{
    public const string DuplicateComputerNames = "duplicate-computer-names";
    public const string TemporaryAssets = "temporary-assets";
    public const string LongRepair = "long-repair";
    public const string Backup = "backup";
    public const string DiskSpace = "disk-space";
    public const string DuplicateSerials = "duplicate-serials";
    public const string LinkedPlaceMismatch = "linked-place-mismatch";
    public const string HubWithoutComponents = "hub-without-components";
}

public sealed class AlertItem
{
    public string Key { get; init; } = "";
    public string Severity { get; init; } = AlertSeverity.Info;
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public int Count { get; init; }
    public List<AlertEntry> Entries { get; init; } = [];
}

public sealed class AlertEntry
{
    public string Text { get; init; } = "";
    public string? Sub { get; init; }
    public Guid? EquipmentId { get; init; }
}
