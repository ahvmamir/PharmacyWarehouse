using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Tests;

public class StockRulesTests
{
    [Theory]
    [InlineData(StockMovementKind.Import, "ورود از Excel")]
    [InlineData(StockMovementKind.Receipt, "ورود جنس")]
    [InlineData(StockMovementKind.Withdrawal, "برداشت")]
    [InlineData(StockMovementKind.Reversal, "برگشت")]
    [InlineData(StockMovementKind.Adjustment, "اصلاح")]
    [InlineData(StockMovementKind.Disposal, "اسقاط")]
    public void MovementLabels_MapEveryKind(StockMovementKind kind, string expected) =>
        Assert.Equal(expected, MovementLabels.Kind(kind));

    [Fact]
    public void MovementLabels_CoverWholeEnum()
    {
        foreach (var kind in Enum.GetValues<StockMovementKind>())
        {
            Assert.NotEqual(kind.ToString(), MovementLabels.Kind(kind));
        }
    }

    [Theory]
    [InlineData(DisposalReason.Expired, "منقضی")]
    [InlineData(DisposalReason.Damaged, "آسیب‌دیده")]
    [InlineData(DisposalReason.Other, "دیگر")]
    public void Disposal_LabelAndMovementNote(DisposalReason reason, string label)
    {
        Assert.Equal(label, DisposalRules.Label(reason));
        Assert.Equal($"{label}: TEST", DisposalRules.MovementNote(reason, "TEST"));
    }

    [Fact]
    public void LocationLabel_IsAsciiWithPrefix_AndMatchesOnlyItself()
    {
        var id = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890");
        var code = LocationLabel.CodeOf(id);

        Assert.Equal("LOC-ABCDEF12", code);
        Assert.True(LocationLabel.Matches(id, code));
        Assert.True(LocationLabel.Matches(id, $" {code} "));
        Assert.False(LocationLabel.Matches(Guid.NewGuid(), code));
        Assert.False(LocationLabel.Matches(id, null));
        Assert.False(LocationLabel.Matches(id, ""));
    }

    [Theory]
    [InlineData(1, "PW-00001")]
    [InlineData(123, "PW-00123")]
    [InlineData(100000, "PW-100000")]
    public void InternalBarcode_Format(int number, string expected) =>
        Assert.Equal(expected, InternalBarcode.Format(number));

    [Theory]
    [InlineData("PW-00001", true)]
    [InlineData("6260000000001", false)]
    [InlineData(null, false)]
    public void InternalBarcode_IsInternal(string? key, bool expected) =>
        Assert.Equal(expected, InternalBarcode.IsInternal(key));

    [Fact]
    public void InternalBarcode_NextNumber_IsHighestPlusOne_IgnoringOtherCodes()
    {
        Assert.Equal(1, InternalBarcode.NextNumber([]));
        Assert.Equal(1, InternalBarcode.NextNumber(["6260000000001", "PW-ABC"]));
        Assert.Equal(8, InternalBarcode.NextNumber(["PW-00003", "PW-00007", "PW-ABC", "6260000000001"]));
    }
}
