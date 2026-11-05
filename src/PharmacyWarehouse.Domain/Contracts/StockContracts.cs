using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

// اصلاح: تعداد واقعی شمرده‌شده می‌آید و سرور تفاوت را حرکت Adjustment می‌کند.
public sealed class AdjustBatchRequest
{
    public int Counted { get; set; }
    public string? Note { get; set; }
    public int Version { get; set; }
}

public sealed class DisposeBatchRequest
{
    public int Quantity { get; set; }
    public DisposalReason Reason { get; set; }
    public string? Note { get; set; }
    public int Version { get; set; }
}

public sealed class StartStocktakeRequest
{
    public Guid LocationId { get; set; }
}

public sealed class StocktakeCountRequest
{
    public Guid BatchId { get; set; }
    public int Counted { get; set; }
}

public sealed record StocktakeListItem(
    Guid Id, Guid LocationId, string LocationPath, string StartedByName, DateTime StartedAtUtc, DateTime? ClosedAtUtc,
    int CountedBatches, int Differences, int Version);

// آخرین شمارش هر بچ در نوبت. Difference همان Counted - ExpectedOnHand در لحظه‌ی شمارش است.
public sealed record StocktakeCountItem(
    Guid Id, Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, string? LocationPath,
    int ExpectedOnHand, int Counted, int Difference, DateTime AtUtc, string UserName, bool IsAdjusted, int Version);

public sealed record StocktakeBatchItem(
    Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, string? LocationPath, int OnHand);

public sealed class StocktakeDetail
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string LocationPath { get; set; } = "";
    public string StartedByName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public int Version { get; set; }
    public List<StocktakeCountItem> Counts { get; set; } = [];

    // بچ‌های دارای موجودی در این محل که هنوز شمرده نشده‌اند (احتمال جا افتادن).
    public List<StocktakeBatchItem> Missing { get; set; } = [];
}

public sealed record MovementReportItem(
    Guid Id, DateTime AtUtc, DateTime OccurredAtUtc, StockMovementKind Kind, Guid ProductId, string ProductName, string? Brand,
    string BatchNumber, int Quantity, string UserName, string? Note);

public sealed record MovementReportResponse(int Total, IReadOnlyList<MovementReportItem> Items);

public sealed record ConsumptionReportResponse(int Days, DashboardConsumption Consumption);

// Code فقط وقتی پر است که قلم کد داخلی گرفته باشد.
public sealed record ProductLabelItem(Guid ProductId, string Name, string? Brand, string? PackDescription, string? Code);

public sealed class GenerateLabelsRequest
{
    public const int MaxProducts = 500;

    public List<Guid>? ProductIds { get; set; }
}
