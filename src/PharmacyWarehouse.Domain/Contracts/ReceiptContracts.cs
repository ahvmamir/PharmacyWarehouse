using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Contracts;

// Expiry متن ورودی است (هر قالبی که ExpiryDate.Parse بپذیرد)؛ خالی برای بچ موجود یعنی «همان تاریخ ثبت‌شده».
// BatchVersion برای بچ موجود از BatchItem.Version است که کلاینت دیده؛ برای بچ تازه null.
public sealed class ReceiptRequest
{
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = "";
    public string? Expiry { get; set; }
    public Guid? LocationId { get; set; }
    public long? UnitCost { get; set; }
    public int Quantity { get; set; }
    public string? Note { get; set; }
    public int? BatchVersion { get; set; }
}

public sealed record ReceiptResult(BatchItem Batch, bool CreatedBatch, IReadOnlyList<ExpiryWarning> Warnings);
