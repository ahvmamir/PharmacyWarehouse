using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Contracts;

// Quantity در Available صفر است. IsExpired نسبت به امروز.
public sealed record AllocationLineItem(
    Guid BatchId, string BatchNumber, string? ExpiryText, string? LocationPath, int Quantity, int OnHand, bool IsExpired, int Version);

// Lines = پیشنهاد FEFO (فقط بچ‌های منقضی‌نشده). Shortfall > 0 یعنی موجودی قابل‌استفاده کم است.
// Available = همه‌ی بچ‌های دارای موجودی برای انتخاب دستی (منقضی‌ها آخر). «منقضی» نسبت به زمان برداشت (minutesAgo) سنجیده می‌شود.
// توجه: صفحه‌ی برداشت برای یافتن قلم (اسکن/جست‌وجو) هنوز به stock.view نیاز دارد؛ خودِ allocation و ثبت نه.
public sealed record AllocationResponse(
    Guid ProductId, string ProductName, string? Brand, string? PackDescription, bool AllowExpiredWithdrawal,
    IReadOnlyList<AllocationLineItem> Lines, IReadOnlyList<AllocationLineItem> Available, int Shortfall, int UsableOnHand);

public sealed class WithdrawalLineRequest
{
    public Guid BatchId { get; set; }
    public int Quantity { get; set; }
    // Version بچ، همان که Client دیده است.
    public int BatchVersion { get; set; }
}

// OccurredAtUtc خالی = اکنون. بخش ۷.۵ spec: حداکثر ۷ روز قبل، آینده رد.
public sealed class WithdrawalRequest
{
    public Guid ProductId { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    // اگر پر باشد بر OccurredAtUtc ارجح است و سرور «اکنون منهای این دقیقه» را حساب می‌کند؛ وابسته به ساعت دستگاه نیست.
    public int? MinutesAgo { get; set; }
    public string? Note { get; set; }
    public List<WithdrawalLineRequest> Lines { get; set; } = [];
}

public sealed record WithdrawalLineItem(Guid BatchId, string BatchNumber, string? ExpiryText, bool IsExpired, int Quantity);

public sealed record WithdrawalItem(
    Guid Id, Guid ProductId, string ProductName, string? Brand, string? PackDescription, int TotalQuantity,
    DateTime AtUtc, DateTime OccurredAtUtc, bool IsLate, bool AfterStocktake, bool ContainsExpired,
    WithdrawalStatus Status, string UserName, string? Note, string? ReviewNote, string? ReviewedByName, DateTime? ReviewedAtUtc,
    bool SelfReviewed, IReadOnlyList<WithdrawalLineItem> Lines, int Version);
