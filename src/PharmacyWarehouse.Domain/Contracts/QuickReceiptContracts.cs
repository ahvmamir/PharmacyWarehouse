using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

// ورود جنس ساده توسط پرسنل (بخش ۱۵.۲ spec). هر ردیف: قلم موجود (ProductId) یا نام آزاد تازه (NewName)، نه هر دو.
public sealed class QuickReceiptLineRequest
{
    public Guid? ProductId { get; set; }
    public string? NewName { get; set; }
    public string BatchNumber { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class QuickReceiptRequest
{
    public const int MaxLines = 100;

    public List<QuickReceiptLineRequest>? Lines { get; set; }
    public string? Note { get; set; }
}

// BatchLocationId محل خود بچ است (null = محل قلم). ExpiryText فقط نمایشی است.
public sealed record ReceiptLineItem(
    Guid Id, Guid ProductId, string ProductName, string? Brand, string? PackDescription, bool IsProvisional, bool RequiresExpiry,
    Guid? ProductLocationId, int ProductVersion, Guid BatchId, string BatchNumber, string? ExpiryText, Guid? BatchLocationId,
    string? LocationPath, int Quantity, int OnHand, int BatchVersion, int Version);

public sealed record ReceiptItem(
    Guid Id, string UserName, DateTime AtUtc, string? Note, ReceiptStatus Status, string? ReviewNote, string? ReviewedByName,
    DateTime? ReviewedAtUtc, IReadOnlyList<ReceiptLineItem> Lines, int Version);

// ویرایش قلم در بررسی؛ همه‌ی فیلدها حالت دلخواه نهایی‌اند (Client با مقدار فعلی پر می‌کند). Version = ProductVersion ردیف.
public sealed class ReviewProductEdit
{
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? PackDescription { get; set; }
    public Guid? LocationId { get; set; }
    public bool RequiresExpiry { get; set; }
    public int Version { get; set; }
}

// MergeIntoProductId: ادغام قلم موقت این ردیف در قلم موجود (با Product هم‌زمان نمی‌آید).
// Expiry خالی = بدون تغییر. BatchLocationId همیشه اعمال می‌شود (null = محل قلم).
public sealed class ReviewLineEdit
{
    public Guid LineId { get; set; }
    public int LineVersion { get; set; }
    public int BatchVersion { get; set; }
    public int Quantity { get; set; }
    public Guid? MergeIntoProductId { get; set; }
    public ReviewProductEdit? Product { get; set; }
    public string? Expiry { get; set; }
    public Guid? BatchLocationId { get; set; }
}

public sealed class ReviewReceiptRequest
{
    public int Version { get; set; }
    public List<ReviewLineEdit> Lines { get; set; } = [];
}
