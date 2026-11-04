using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

public sealed class SaveProductRequest
{
    public string Name { get; set; } = "";
    public string? Brand { get; set; }
    public string? PackDescription { get; set; }
    public Guid? LocationId { get; set; }
    public int? MinStock { get; set; }
    public bool RequiresExpiry { get; set; } = true;
    public bool AllowExpiredWithdrawal { get; set; }
    public int Version { get; set; }
}

public sealed class AddBarcodeRequest
{
    public string Code { get; set; } = "";
}

// UsableOnHand: موجودی بچ‌های منقضی‌نشده و بی‌تاریخ (امروز به وقت LocalDay).
public sealed record ProductListItem(
    Guid Id, string Name, string? Brand, string? PackDescription, string? LocationPath,
    int OnHand, int UsableOnHand, int? MinStock, DateOnly? NearestExpiry, bool IsActive, int Version);

public sealed record BatchItem(
    Guid Id, string BatchNumber, DateOnly? ExpiryDate, ExpiryPrecision Precision, CalendarKind Calendar,
    string? ExpiryText, string? LocationPath, int OnHand, bool IsExpired, int Version);

public sealed record BarcodeItem(Guid Id, string Code);

public sealed record MovementItem(
    Guid Id, Guid BatchId, string BatchNumber, StockMovementKind Kind, int Quantity,
    DateTime AtUtc, DateTime OccurredAtUtc, string UserName, string? Note);

public sealed class ProductDetail
{
    public ProductListItem Summary { get; set; } = null!;
    public bool RequiresExpiry { get; set; }
    public bool AllowExpiredWithdrawal { get; set; }
    public Guid? LocationId { get; set; }
    public List<BatchItem> Batches { get; set; } = [];
    public List<BarcodeItem> Barcodes { get; set; } = [];
}

public sealed class ProductHistory
{
    public List<MovementItem> Movements { get; set; } = [];
    public List<AuditEntryItem> Changes { get; set; } = [];
}
