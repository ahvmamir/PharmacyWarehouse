namespace PharmacyWarehouse.Domain.Stock;

// موجودی بعد از هر حرکت: جمع تجمعی Quantity به ترتیب ثبت (AtUtc، سپس Id).
// ورودی باید همه‌ی حرکت‌های قلم باشد (نه صفحه یا فیلتر نوع)، وگرنه موجودی وسط راه غلط می‌شود.
public static class MovementBalance
{
    public sealed record Row(Guid Id, Guid BatchId, DateTime AtUtc, int Quantity);

    // کلید هر دو دیکشنری شناسه‌ی حرکت است: Product = موجودی کل قلم، Batch = موجودی همان بچ.
    public sealed record Result(IReadOnlyDictionary<Guid, int> Product, IReadOnlyDictionary<Guid, int> Batch);

    public static Result After(IEnumerable<Row> movements)
    {
        var productTotal = 0;
        var batchTotals = new Dictionary<Guid, int>();
        var productAfter = new Dictionary<Guid, int>();
        var batchAfter = new Dictionary<Guid, int>();

        foreach (var m in movements.OrderBy(m => m.AtUtc).ThenBy(m => m.Id))
        {
            productTotal += m.Quantity;
            var batchTotal = batchTotals.GetValueOrDefault(m.BatchId) + m.Quantity;
            batchTotals[m.BatchId] = batchTotal;
            productAfter[m.Id] = productTotal;
            batchAfter[m.Id] = batchTotal;
        }

        return new Result(productAfter, batchAfter);
    }
}
