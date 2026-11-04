using PharmacyWarehouse.Domain.Contracts;
using Xunit;

namespace PharmacyWarehouse.Domain.Tests;

public class HealthResponseTests
{
    [Fact]
    public void Ok_SetsOkStatusAndKeepsGivenTime()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var result = HealthResponse.Ok(now);

        Assert.Equal("ok", result.Status);
        Assert.Equal(now, result.ServerTimeUtc);
    }

    [Fact]
    public void HealthRoute_StartsWithSlash()
    {
        Assert.StartsWith("/", ApiRoutes.Health);
    }
}