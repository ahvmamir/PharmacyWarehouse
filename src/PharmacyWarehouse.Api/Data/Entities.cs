
namespace PharmacyWarehouse.Api.Data;

// هر موجودیتی که تغییرش باید در تاریخچه بیاید. Version توکن هم‌زمانی است و رهگیر آن را زیاد می‌کند.
public interface IAudited
{
    Guid Id { get; }
    int Version { get; set; }
}

// گره درخت محل. ParentKey همان ParentId به‌صورت متن است ("" برای ریشه)، چون SQLite در unique index
// مقدارهای NULL را برابر نمی‌داند و دو ریشه‌ی هم‌نام را می‌پذیرفت.
public class StorageLocation : IAudited
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string ParentKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

// فقط اضافه‌شونده؛ trigger جلوی UPDATE و DELETE را می‌گیرد.
public class AuditEntry
{
    public const int MaxReason = 500;

    public Guid Id { get; set; }
    public Guid ChangeSetId { get; set; }
    public DateTime AtUtc { get; set; }
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    // رکوردی که این ردیف در تاریخچه‌ی آن می‌آید (AuditSubject.Of)؛ مثلاً بچ در تاریخچه‌ی قلمش.
    public Guid? SubjectId { get; set; }

    // برچسب کوتاه برای ردیف‌های فرعی؛ فعلاً خالی.
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
