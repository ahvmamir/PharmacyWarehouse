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

// ثبت گروهی ورود جنس: همه‌ی ردیف‌ها در یک تراکنش. Lines به ترتیب فهرست صفحه است و خطاها با شماره‌ی ردیف برمی‌گردند.
public sealed class BulkReceiptRequest
{
    public const int MaxLines = 300;

    public List<ReceiptRequest>? Lines { get; set; }
}

public sealed record BulkReceiptResult(IReadOnlyList<ReceiptResult> Lines);

// کلید خطای هر ردیف در Errors پاسخ: «Lines[2].Expiry». یک قرارداد برای سرور و کلاینت.
public static class BulkReceiptErrors
{
    public static string Key(int line, string field) => $"Lines[{line}].{field}";

    public static bool TryParse(string key, out int line, out string field)
    {
        line = -1;
        field = "";

        const string prefix = "Lines[";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var close = key.IndexOf(']');
        if (close < 0 || !int.TryParse(key.AsSpan(prefix.Length, close - prefix.Length), out line)) return false;

        field = close + 2 <= key.Length ? key[(close + 2)..] : "";
        return true;
    }
}
