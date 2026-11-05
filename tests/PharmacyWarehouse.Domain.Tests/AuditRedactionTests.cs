using System.Text.Json;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Domain.Tests;

public class AuditRedactionTests
{
    [Fact]
    public void WithoutKey_RemovesOnlyThatKey_KeepsPersianReadable()
    {
        var json = "{\"BatchNumber\":\"TEST-1\",\"UnitCost\":\"1250000\",\"Note\":\"داروی آزمایشی\"}";

        var result = AuditRedaction.WithoutKey(json, "UnitCost");

        var values = JsonSerializer.Deserialize<Dictionary<string, string?>>(result!)!;
        Assert.False(values.ContainsKey("UnitCost"));
        Assert.Equal("TEST-1", values["BatchNumber"]);
        Assert.Contains("داروی آزمایشی", result); // escape نشده
        Assert.DoesNotContain("1250000", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void WithoutKey_NonJson_IsReturnedUntouched(string? json) =>
        Assert.Equal(json, AuditRedaction.WithoutKey(json, "UnitCost"));

    [Fact]
    public void WithoutKey_MissingKey_ReturnsEquivalentJson()
    {
        var json = "{\"A\":\"1\"}";
        Assert.Equal(json, AuditRedaction.WithoutKey(json, "UnitCost"));
    }
}
