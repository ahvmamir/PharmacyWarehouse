using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

// دوره‌ی انبارگردانی (بخش ۱۵.۳ spec). LocationIds و CategoryIds خالی یعنی «همه‌ی محل‌ها» و «همه‌ی دسته‌ها».
public sealed class SavePeriodRequest
{
    public string Name { get; set; } = "";
    public List<Guid> LocationIds { get; set; } = [];
    public List<Guid> CategoryIds { get; set; } = [];
    public int Version { get; set; }
}

public sealed record PeriodListItem(
    Guid Id, string Name, bool IsOpen, DateTime StartedAtUtc, DateTime? ClosedAtUtc, string ScopeText,
    int Sessions, int OpenSessions, int Discrepancies, int Version);

// یک بچ در دامنه‌ی دوره با آخرین شمارشش در کل دوره. Conflicting: در دو نوبت مختلف دو عدد متفاوت شمرده شده.
public sealed record PeriodRowItem(
    Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, string? ExpiryText, string? LocationPath, int OnHand,
    PeriodRowStatus Status, Guid? CountId, int? CountVersion, Guid? StocktakeId, int? Expected, int? Counted, int? Difference,
    DateTime? CountedAtUtc, string? CountedBy, bool Conflicting, string? DismissNote);

public sealed record PeriodSummary(int NotCounted, int Pending, int Matched, int Discrepancy, int Adjusted, int Dismissed)
{
    public int Total => NotCounted + Pending + Matched + Discrepancy + Adjusted + Dismissed;
    public int Counted => Total - NotCounted;
}

public sealed class PeriodDetail
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsOpen { get; set; }
    public bool IsLegacy { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public string StartedByName { get; set; } = "";
    public int Version { get; set; }
    public List<Guid> LocationIds { get; set; } = [];
    public List<string> LocationPaths { get; set; } = [];
    public List<Guid> CategoryIds { get; set; } = [];
    public List<string> CategoryNames { get; set; } = [];
    public List<StocktakeListItem> Sessions { get; set; } = [];
    public List<PeriodRowItem> Rows { get; set; } = [];
    public PeriodSummary Summary { get; set; } = new(0, 0, 0, 0, 0, 0);
}

// رفع گروهی اختلاف: اصلاح (حرکت Adjustment) یا صرف‌نظر با یادداشت. نتیجه جزئی است، مثل تأیید گروهی برداشت.
public sealed class ResolveCountsRequest
{
    public const int MaxItems = 300;

    public List<ResolveCountItem> Items { get; set; } = [];
}

public sealed class ResolveCountItem
{
    public Guid CountId { get; set; }
    public int Version { get; set; }
    public bool Dismiss { get; set; }
    public string? Note { get; set; }
}

public sealed record ResolveCountsResult(IReadOnlyList<Guid> Done, IReadOnlyList<BulkFailure> Failed);
