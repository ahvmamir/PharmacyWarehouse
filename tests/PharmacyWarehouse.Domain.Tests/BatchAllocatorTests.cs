using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Tests;

public class BatchAllocatorTests
{
    private static readonly DateOnly Today = new(2030, 1, 10);
    private static readonly DateTime T0 = new(2029, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static AllocatableBatch B(string n, DateOnly? expiry, int onHand, int createdDay = 0, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), n, expiry, T0.AddDays(createdDay), onHand);

    [Fact]
    public void Suggested_OrdersByExpiry_NoDateLast()
    {
        var noDate = B("N", null, 5);
        var late = B("L", new DateOnly(2031, 1, 1), 5);
        var soon = B("S", new DateOnly(2030, 2, 1), 5);

        var order = BatchAllocator.FefoOrder([noDate, late, soon], Today).Select(b => b.BatchNumber);

        Assert.Equal(["S", "L", "N"], order);
    }

    [Fact]
    public void Suggested_TieOnExpiry_BreaksByCreatedThenId()
    {
        var date = new DateOnly(2030, 6, 1);
        var low = new Guid("00000000-0000-0000-0000-000000000001");
        var high = new Guid("00000000-0000-0000-0000-000000000002");
        var newer = B("newer", date, 1, createdDay: 5, id: low);
        var olderHighId = B("olderB", date, 1, createdDay: 1, id: high);
        var olderLowId = B("olderA", date, 1, createdDay: 1, id: low);

        var order = BatchAllocator.FefoOrder([newer, olderHighId, olderLowId], Today).Select(b => b.BatchNumber);

        Assert.Equal(["olderA", "olderB", "newer"], order);
    }

    [Fact]
    public void Suggested_ExcludesExpiredAndEmpty_ButKeepsExpiringToday()
    {
        var expired = B("E", new DateOnly(2030, 1, 9), 5);
        var today = B("T", Today, 5);
        var empty = B("Z", new DateOnly(2031, 1, 1), 0);

        var order = BatchAllocator.FefoOrder([expired, today, empty], Today).Select(b => b.BatchNumber);

        Assert.Equal(["T"], order);
    }

    [Fact]
    public void Allocate_SplitsAcrossBatchesInFefoOrder()
    {
        var a = B("A", new DateOnly(2030, 2, 1), 3);
        var b = B("B", new DateOnly(2030, 3, 1), 10);

        var result = BatchAllocator.Allocate([b, a], 5, Today);

        Assert.Equal(0, result.Shortfall);
        Assert.Collection(result.Lines,
            l => { Assert.Equal(a.Id, l.BatchId); Assert.Equal(3, l.Quantity); },
            l => { Assert.Equal(b.Id, l.BatchId); Assert.Equal(2, l.Quantity); });
    }

    [Fact]
    public void Allocate_Insufficient_ReportsShortfallAndTakesEverything()
    {
        var a = B("A", new DateOnly(2030, 2, 1), 3);
        var expired = B("E", new DateOnly(2029, 12, 1), 100);

        var result = BatchAllocator.Allocate([a, expired], 8, Today);

        Assert.Equal(5, result.Shortfall);
        Assert.Single(result.Lines);
        Assert.Equal(3, result.Lines[0].Quantity);
    }

    [Fact]
    public void Allocate_ExactAmount_HasNoShortfall()
    {
        var a = B("A", new DateOnly(2030, 2, 1), 4);
        var result = BatchAllocator.Allocate([a], 4, Today);
        Assert.Equal(0, result.Shortfall);
        Assert.Equal(4, result.Lines.Sum(l => l.Quantity));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Allocate_NonPositiveQuantity_Throws(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BatchAllocator.Allocate([], quantity, Today));
}
