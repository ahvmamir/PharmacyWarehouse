using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Tests;

public class DashboardRulesTests
{
    private static readonly DateOnly Today = new(2028, 1, 31);

    [Theory]
    [InlineData(null, ExpiryBucket.None)]
    [InlineData("2028-01-30", ExpiryBucket.Expired)]
    [InlineData("2028-01-31", ExpiryBucket.Near)] // روز انقضا هنوز منقضی نیست
    [InlineData("2028-04-30", ExpiryBucket.Near)] // ۳۱ ژانویه + ۳ ماه = آخر آوریل
    [InlineData("2028-05-01", ExpiryBucket.Far)]
    [InlineData("2028-07-31", ExpiryBucket.Far)]
    [InlineData("2028-08-01", ExpiryBucket.None)]
    public void Classify_UsesCalendarMonths(string? expiry, ExpiryBucket expected)
    {
        var date = expiry is null ? (DateOnly?)null : DateOnly.Parse(expiry);

        Assert.Equal(expected, DashboardRules.Classify(date, Today, 3, 6));
    }

    [Theory]
    [InlineData(3, 6, true)]
    [InlineData(1, 60, true)]
    [InlineData(6, 6, false)]
    [InlineData(6, 3, false)]
    [InlineData(0, 6, false)]
    [InlineData(-1, 6, false)]
    [InlineData(3, 61, false)]
    public void ValidateThresholds(int near, int far, bool valid) =>
        Assert.Equal(valid, DashboardRules.ValidateThresholds(near, far) is null);

    [Theory]
    [InlineData(null, 0, false)]
    [InlineData(5, 0, true)]
    [InlineData(5, 4, true)]
    [InlineData(5, 5, false)]
    [InlineData(0, 0, false)]
    public void IsBelowMinimum(int? min, int onHand, bool expected) =>
        Assert.Equal(expected, DashboardRules.IsBelowMinimum(min, onHand));
}
