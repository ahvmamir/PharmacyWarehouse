namespace PharmacyWarehouse.Domain.Contracts;

// شمارنده‌ی کارتابل (بخش ۱۵.۳ spec). هر عدد فقط برای کسی که مجوز همان صف را دارد پر می‌شود؛ وگرنه null.
public sealed class InboxCounts
{
    public int? PendingWithdrawals { get; init; }
    public int? PendingReceipts { get; init; }
}
