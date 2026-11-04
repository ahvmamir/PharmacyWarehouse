using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Domain.Tests;

public class LoginRequestTests
{
    [Fact]
    public void ToString_HidesUsernameAndPassword()
    {
        var request = new LoginRequest { Username = "test-user", Password = "test-secret" };

        var text = request.ToString();

        Assert.DoesNotContain("test-user", text);
        Assert.DoesNotContain("test-secret", text);
    }
}
