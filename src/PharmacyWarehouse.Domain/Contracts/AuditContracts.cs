namespace PharmacyWarehouse.Domain.Contracts;

// یک ردیف تاریخچه برای نمایش.
public sealed class AuditEntryItem
{
    public Guid Id { get; init; }
    public Guid ChangeSetId { get; init; }
    public DateTime AtUtc { get; init; }
    public string UserName { get; init; } = "";
    public string EntityType { get; init; } = "";
    public string? EntityLabel { get; init; }
    public string Action { get; init; } = "";
    public string? Field { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public string? Reason { get; init; }

    // ردیفی که مال دستگاه دیگری است ولی به این دستگاه مربوط است (وصل یا جدا شدن آن)، مثل «مانیتور TEST-2».
    public string? Subject { get; init; }

    // متن خوانای مقدار، وقتی خود مقدار شناسه است (مثلاً «متصل به» ← «کیس TEST-1»).
    public string? OldText { get; init; }
    public string? NewText { get; init; }
}
