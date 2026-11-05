using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

public sealed record DashboardExpiryItem(Guid BatchId, Guid ProductId, string ProductName, string? Brand, string BatchNumber, DateOnly ExpiryDate, string ExpiryText, string? LocationPath, int OnHand);

// Total تعداد واقعی است؛ Items حداکثر تا سقف پاسخ می‌آید.
public sealed record DashboardExpiryBucket(int Total, IReadOnlyList<DashboardExpiryItem> Items);

public sealed record DashboardExpiry(int NearMonths, int FarMonths, DashboardExpiryBucket Expired, DashboardExpiryBucket Near, DashboardExpiryBucket Far);

public sealed record DashboardBelowMinItem(Guid ProductId, string ProductName, string? Brand, int MinStock, int UnexpiredOnHand);

public sealed record DashboardConsumptionItem(Guid Id, string Name, int Quantity);

public sealed record DashboardConsumption(IReadOnlyList<DashboardConsumptionItem> TopProducts, IReadOnlyList<DashboardConsumptionItem> ByUser);

public sealed record DashboardUnreviewed(int PendingCount, DateTime? OldestPendingAtUtc, int SelfReviewed30Days, int Late30Days, int AfterStocktake30Days);

public sealed record DashboardCorrectionItem(Guid MovementId, StockMovementKind Kind, DateTime AtUtc, string ProductName, string BatchNumber, int Quantity, string? UserName, string? Note);

public sealed record DashboardLastStocktake(Guid LocationId, string LocationName, DateTime? CompletedAtUtc);

public sealed record DashboardResponse(
    DashboardExpiry Expiry,
    IReadOnlyList<DashboardBelowMinItem> BelowMinimum,
    DashboardConsumption Consumption7Days,
    DashboardConsumption Consumption30Days,
    DashboardUnreviewed Unreviewed,
    IReadOnlyList<DashboardCorrectionItem> Corrections30Days,
    IReadOnlyList<DashboardLastStocktake> LastStocktakes);

// هر آستانه یک ردیف AppSetting با Version خودش است.
public sealed record AppSettingsResponse(int NearMonths, int FarMonths, int NearVersion, int FarVersion);

public sealed class SaveAppSettingsRequest
{
    public int NearMonths { get; set; }
    public int FarMonths { get; set; }
    public int NearVersion { get; set; }
    public int FarVersion { get; set; }
}
