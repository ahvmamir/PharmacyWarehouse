using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

public class LocalDayTests
{
    [Fact]
    public void Bounds_ForAnOrdinaryTime_AreLocalMidnightToLocalMidnight()
    {
        // 10:00 UTC = 13:30 local (+03:30); local midnight = 20:30 UTC of the previous day.
        var now = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);

        var (start, end) = LocalDay.BoundsUtc(now);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 30, 0, TimeSpan.Zero), end);
        Assert.Equal(TimeSpan.Zero, start.Offset);
        Assert.Equal(TimeSpan.Zero, end.Offset);
    }

    [Fact]
    public void Bounds_OneSecondBeforeLocalMidnight_StillBelongToTheSameDay()
    {
        var now = new DateTimeOffset(2026, 11, 1, 20, 29, 59, TimeSpan.Zero);

        var (start, end) = LocalDay.BoundsUtc(now);

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 20, 30, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 30, 0, TimeSpan.Zero), end);
    }

    [Fact]
    public void Bounds_ExactlyAtLocalMidnight_StartTheNextDay()
    {
        var now = new DateTimeOffset(2026, 11, 1, 20, 30, 0, TimeSpan.Zero);

        var (start, end) = LocalDay.BoundsUtc(now);

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 20, 30, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 20, 30, 0, TimeSpan.Zero), end);
    }
}