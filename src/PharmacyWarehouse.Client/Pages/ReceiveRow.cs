using System.Text.Json.Serialization;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Pages;

// یک ردیف فهرست ورود جنس. فیلدهای بدون JsonIgnore پیش‌نویس localStorage را می‌سازند،
// پس با تازه‌سازی صفحه از بین نمی‌روند؛ بقیه هنگام بارگذاری از سرور تازه می‌شوند.
public sealed class ReceiveRow
{
    public Guid Key { get; set; } = Guid.NewGuid();
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = "";

    // null یعنی «بچ تازه».
    public Guid? BatchId { get; set; }
    public string BatchNumber { get; set; } = "";
    public string? Expiry { get; set; }
    public Guid? LocationId { get; set; }
    public string CostText { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Note { get; set; } = "";

    [JsonIgnore] public ProductDetail? Detail { get; set; }
    [JsonIgnore] public bool ChangingLocation { get; set; }
    [JsonIgnore] public Dictionary<string, string[]> Errors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [JsonIgnore] public string? RowError { get; set; }

    [JsonIgnore] public BatchItem? SelectedBatch => Detail?.Batches.FirstOrDefault(b => b.Id == BatchId);
    [JsonIgnore] public bool HasError => RowError is not null || Errors.Count > 0;

    // فقط برای نمایش «ناقص»؛ مرجع اعتبار، سرور است.
    [JsonIgnore]
    public bool IsComplete => Detail is not null && Quantity >= 1 && (BatchId is not null
        || (!string.IsNullOrWhiteSpace(BatchNumber) && (!Detail.RequiresExpiry || !string.IsNullOrWhiteSpace(Expiry))));

    public void ClearErrors()
    {
        Errors = new(StringComparer.OrdinalIgnoreCase);
        RowError = null;
    }
}
