namespace PharmacyWarehouse.Client.Services;

// نوبت جاری انبارگردانی در همین زبانه‌ی مرورگر؛ رفتن به صفحه‌ی دیگر و برگشتن آن را از دست نمی‌دهد.
// «دیده‌شده» از زمان تأیید ثبت‌شده در سرور حساب می‌شود، نه از این حافظه.
public sealed class StocktakeState
{
    public sealed record LogLine(string Kind, string Text, string? Note, Guid? EquipmentId);

    public DateTime? StartedAtUtc { get; private set; }
    public Guid? UnitId { get; private set; }
    public Guid? BuildingId { get; private set; }
    public List<LogLine> Log { get; } = [];

    // زمان شروع از ساعت سرور (همان ساعتی که زمان تأیید را ثبت می‌کند)؛ ساعت غلط کامپیوتر کاربر اثری ندارد.
    public void Start(Guid? unitId, Guid? buildingId, DateTime serverNowUtc)
    {
        StartedAtUtc = serverNowUtc.AddSeconds(-5);
        UnitId = unitId;
        BuildingId = buildingId;
        Log.Clear();
    }

    public void End()
    {
        StartedAtUtc = null;
        Log.Clear();
    }

    public void Add(string kind, string text, string? note, Guid? equipmentId) =>
        Log.Insert(0, new LogLine(kind, text, note, equipmentId));
}
