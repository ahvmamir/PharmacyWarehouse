using PharmacyWarehouse.Domain.Auth;

namespace PharmacyWarehouse.Domain.Tests;

public class PermissionsTests
{
    [Fact]
    public void AllKeys_AreUnique()
    {
        Assert.Equal(Permissions.All.Count, Permissions.All.Select(p => p.Key).Distinct().Count());
    }

    // بخش ۱۱ spec؛ هر مجوز تازه باید آگاهانه این‌جا هم بیاید.
    [Fact]
    public void AllKeys_MatchSpecSection11()
    {
        string[] expected =
        [
            "stock.view", "withdrawal.create", "withdrawal.review", "stock.receive", "stock.adjust", "products.manage",
            "dashboard.view", "reports.view", "data.import", "data.export", "backup.download", "settings.manage",
            "users.manage", "roles.manage", "receipt.submit"
        ];

        Assert.Equal(expected.Order(), Permissions.All.Select(p => p.Key).Order());
    }

    [Fact]
    public void DefaultRoles_AreTheFourOfTheSpec()
    {
        Assert.Equal(["Admin", "Staff", "Keeper", "Manager"], Roles.Defaults);
    }
}
