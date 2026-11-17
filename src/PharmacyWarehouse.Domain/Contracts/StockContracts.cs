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

// PeriodId اجباری است (بخش ۱۵.۳ spec)؛ CategoryId اختیاری دامنه را به یک دسته‌ی دوره باریک می‌کند.
public sealed class StartStocktakeRequest
{
    public Guid LocationId { get; set; }
    public Guid? PeriodId { get; set; }
    public Guid? CategoryId { get; set; }
}

public sealed class StocktakeCountRequest
{
    public Guid BatchId { get; set; }
    public int Counted { get; set; }
}

public sealed record StocktakeListItem(
    Guid Id, Guid LocationId, string LocationPath, string StartedByName, DateTime StartedAtUtc, DateTime? ClosedAtUtc,
    int CountedBatches, int Differences, int Version, Guid? PeriodId = null, string? PeriodName = null, string? CategoryName = null);

// آخرین شمارش هر بچ در نوبت. Difference همان Counted - ExpectedOnHand در لحظه‌ی شمارش است.
public sealed record StocktakeCountItem(
    Guid Id, Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, string? LocationPath,
    int ExpectedOnHand, int Counted, int Difference, DateTime AtUtc, string UserName, bool IsAdjusted, int Version);

// InScope=false: بچ همین قلم که جای دیگری تعریف شده (یا محل ندارد)؛ فقط «جای نادرست» برایش ثبت می‌شود.
public sealed record StocktakeBatchItem(
    Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, string? LocationPath, int OnHand,
    bool InScope = true, bool IsExpired = false);

// جعبه‌های بچی که در محل این نوبت پیدا شد ولی جایش جای دیگری است (بخش ۱۵.۳ spec). LocationId خالی = محل نوبت.
public sealed class MisplacedCountRequest
{
    public Guid BatchId { get; set; }
    public int Counted { get; set; }
    public Guid? LocationId { get; set; }
}

// بچی که در سامانه نیست و در شمارش پیدا شد: با موجودی ۰ ساخته می‌شود و شمارشش اصلاح مثبت می‌دهد.
public sealed class NewBatchCountRequest
{
    public Guid ProductId { get; set; }
    public string BatchNumber { get; set; } = "";
    public string? Expiry { get; set; }
    public int Counted { get; set; }
}

// Relocate=false: جعبه‌ها به جای بچ برگردانده شد؛ true: محل بچ به محل پیداشده تغییر می‌کند.
public sealed class ResolveMisplacedRequest
{
    public bool Relocate { get; set; }
    public int Version { get; set; }
}

public sealed record MisplacedItem(
    Guid Id, Guid StocktakeId, Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, bool IsExpired,
    string? HomePath, string FoundAtPath, int Counted, int OnHand, DateTime AtUtc, string UserName,
    MisplacedResolution? Resolution, DateTime? ResolvedAtUtc, int Version);

public sealed class StocktakeDetail
{
    public Guid Id { get; set; }
    public Guid LocationId { get; set; }
    public string LocationPath { get; set; } = "";
    public string StartedByName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public int Version { get; set; }
    public Guid PeriodId { get; set; }
    public string PeriodName { get; set; } = "";
    public bool PeriodOpen { get; set; }
    public string? CategoryName { get; set; }
    public List<StocktakeCountItem> Counts { get; set; } = [];

    // بچ‌های دارای موجودی در این محل که هنوز شمرده نشده‌اند (احتمال جا افتادن).
    public List<StocktakeBatchItem> Missing { get; set; } = [];

    // جعبه‌هایی که در این نوبت جای نادرست پیدا شد.
    public List<MisplacedItem> Misplaced { get; set; } = [];
}

public sealed record MovementReportItem(
    Guid Id, DateTime AtUtc, DateTime OccurredAtUtc, StockMovementKind Kind, Guid ProductId, string ProductName, string? Brand,
    string BatchNumber, int Quantity, string UserName, string? Note,
    // فقط وقتی فیلتر قلم ست باشد پر است (موجودی کل قلم بعد از این حرکت).
    int? ProductBalanceAfter);

public sealed record MovementReportResponse(int Total, IReadOnlyList<MovementReportItem> Items);

public sealed record ConsumptionReportResponse(int Days, DashboardConsumption Consumption);

// Code فقط وقتی پر است که قلم کد داخلی گرفته باشد.
public sealed record ProductLabelItem(Guid ProductId, string Name, string? Brand, string? PackDescription, string? Code);

public sealed class GenerateLabelsRequest
{
    public const int MaxProducts = 500;

    public List<Guid>? ProductIds { get; set; }
}
