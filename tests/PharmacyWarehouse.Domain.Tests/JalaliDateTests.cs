using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

public class JalaliDateTests
{
    [Fact]
    public void Format_ConvertsToJalaliWithLocalOffset()
    {
        // 2026-11-03 10:00 UTC = 13:30 local = 1405/08/12.
        var utc = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Utc);

        Assert.Equal("۱۴۰۵/۰۸/۱۲ ۱۳:۳۰", JalaliDate.Format(utc));
    }

    [Fact]
    public void FormatDate_AfterLocalMidnight_IsTheNextDay()
    {
        // 20:30 UTC is exactly local midnight.
        var before = new DateTime(2026, 11, 3, 20, 29, 0, DateTimeKind.Utc);
        var after = new DateTime(2026, 11, 3, 20, 30, 0, DateTimeKind.Utc);

        Assert.Equal("۱۴۰۵/۰۸/۱۲", JalaliDate.FormatDate(before));
        Assert.Equal("۱۴۰۵/۰۸/۱۳", JalaliDate.FormatDate(after));
    }

    [Fact]
    public void FormatDate_NewYear_IsFarvardinFirst()
    {
        // 1405/01/01 began at local midnight on 2026-03-21.
        var utc = new DateTime(2026, 3, 21, 8, 0, 0, DateTimeKind.Utc);

        Assert.Equal("۱۴۰۵/۰۱/۰۱", JalaliDate.FormatDate(utc));
    }
}
