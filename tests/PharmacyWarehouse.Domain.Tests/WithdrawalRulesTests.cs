using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Domain.Tests;

public class WithdrawalRulesTests
{
    private static readonly DateTime Now = new(2030, 1, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Null_MeansNow_NotLate()
    {
        var r = WithdrawalRules.ResolveTime(null, Now);
        Assert.Null(r.Error);
        Assert.Equal(Now, r.OccurredAtUtc);
        Assert.False(r.IsLate);
    }

    [Fact]
    public void ExactlyTwoHoursAgo_IsNotLate_BeyondIsLate()
    {
        Assert.False(WithdrawalRules.ResolveTime(Now.AddHours(-2), Now).IsLate);
        Assert.True(WithdrawalRules.ResolveTime(Now.AddHours(-2).AddMinutes(-1), Now).IsLate);
    }

    [Fact]
    public void SevenDaysAgo_Allowed_BeyondRejected()
    {
        Assert.Null(WithdrawalRules.ResolveTime(Now.AddDays(-7), Now).Error);
        Assert.NotNull(WithdrawalRules.ResolveTime(Now.AddDays(-7).AddMinutes(-1), Now).Error);
    }

    [Fact]
    public void Future_Rejected_ButSmallClockSkewIsClampedToNow()
    {
        Assert.NotNull(WithdrawalRules.ResolveTime(Now.AddMinutes(30), Now).Error);

        var skew = WithdrawalRules.ResolveTime(Now.AddMinutes(3), Now);
        Assert.Null(skew.Error);
        Assert.Equal(Now, skew.OccurredAtUtc);
    }

    [Fact]
    public void NonUtcKind_IsConvertedNotTrusted()
    {
        var local = DateTime.SpecifyKind(Now.AddHours(-1), DateTimeKind.Unspecified);
        var r = WithdrawalRules.ResolveTime(local, Now);
        Assert.Null(r.Error);
        Assert.Equal(Now.AddHours(-1), r.OccurredAtUtc);
    }

    [Fact]
    public void LocalDay_Today_UsesTehranOffset()
    {
        // ۲۰:۳۰ UTC = ۰۰:۰۰ روز بعد به وقت محلی (+۰۳:۳۰)
        Assert.Equal(new DateOnly(2030, 1, 10), LocalDay.Today(new DateTime(2030, 1, 10, 20, 29, 0, DateTimeKind.Utc)));
        Assert.Equal(new DateOnly(2030, 1, 11), LocalDay.Today(new DateTime(2030, 1, 10, 20, 30, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(null, false)]
    public void IsExpired_NullNeverExpires(string? _, bool expected) =>
        Assert.Equal(expected, ExpiryDate.IsExpired(null, new DateOnly(2030, 1, 1)));

    [Fact]
    public void IsExpired_StrictlyBeforeToday()
    {
        var today = new DateOnly(2030, 1, 10);
        Assert.True(ExpiryDate.IsExpired(today.AddDays(-1), today));
        Assert.False(ExpiryDate.IsExpired(today, today));
    }
}
