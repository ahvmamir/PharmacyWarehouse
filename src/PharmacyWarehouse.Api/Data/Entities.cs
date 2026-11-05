
using PharmacyWarehouse.Domain.Stock;

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
    // تنها جای ساختن ParentKey: "" برای ریشه، در غیر این صورت شناسه‌ی پدر.
    public static string KeyOf(Guid? parentId) => parentId?.ToString() ?? "";

    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string ParentKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
}

// قلم (دارو یا کالا). کلیدهای خالی رشته‌ی خالی‌اند، نه NULL (همان دلیل ParentKey)؛ BrandKey و PackKey برای «بی‌برند» و «بی‌بسته‌بندی».
public class Product : IAudited
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string? Brand { get; set; }
    public string BrandKey { get; set; } = "";
    public string? PackDescription { get; set; }
    public string PackKey { get; set; } = "";
    public Guid? LocationId { get; set; }
    public int? MinStock { get; set; }
    public int UnitsPerPack { get; set; } = 1;
    public bool RequiresExpiry { get; set; } = true;
    public bool AllowExpiredWithdrawal { get; set; }
    public bool IsActive { get; set; } = true;
    public string SearchText { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public int Version { get; set; } = 1;
}

public class ProductBarcode : IAudited
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string Code { get; set; } = "";
    public string CodeKey { get; set; } = "";
    public int Version { get; set; } = 1;
}

// OnHand فقط از طریق StockLedger عوض می‌شود و همیشه برابر جمع Quantity حرکت‌های این بچ است.
public class Batch : IAudited
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = "";
    public string BatchKey { get; set; } = "";
    public DateOnly? ExpiryDate { get; set; }
    public ExpiryPrecision ExpiryPrecision { get; set; }
    public CalendarKind ExpiryCalendar { get; set; }
    public Guid? LocationId { get; set; }
    public long? UnitCost { get; set; }
    public int OnHand { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public int Version { get; set; } = 1;
}

// دفتر حرکت؛ IAudited نیست چون خودش تاریخچه است. فقط اضافه‌شونده (trigger).
public class StockMovement
{
    public Guid Id { get; set; }
    public Guid BatchId { get; set; }
    public StockMovementKind Kind { get; set; }
    public int Quantity { get; set; }
    public DateTime AtUtc { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public Guid? UserId { get; set; }
    public Guid? WithdrawalId { get; set; }
    public Guid? StocktakeCountId { get; set; }
    public Guid? ReversesId { get; set; }
    public string? Note { get; set; }
}

// یک برداشت = یک قلم، یک یا چند بچ. حرکت‌های برداشت با WithdrawalId به آن وصل‌اند. حذف ندارد.
public class Withdrawal : IAudited
{
    public const int MaxReviewNote = 500;

    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid UserId { get; set; }
    public DateTime AtUtc { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public bool IsLate { get; set; }
    public bool AfterStocktake { get; set; }
    public WithdrawalStatus Status { get; set; } = WithdrawalStatus.Pending;
    public Guid? ReviewedById { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? ReviewNote { get; set; }
    public bool SelfReviewed { get; set; }
    public int Version { get; set; } = 1;
}

// تنظیم کلید/مقدار؛ مقدار متن است و معنایش را DashboardRules تعیین می‌کند. ردیف‌ها با migration ساخته می‌شوند، پس Version همیشه هست.
public class AppSetting : IAudited
{
    public const int MaxKey = 50;
    public const int MaxValue = 200;

    public Guid Id { get; set; }
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
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
