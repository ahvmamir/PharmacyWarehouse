using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class AlertTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;
    private HttpClient Staff { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        await App.AddStaffAsync("staff-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        Staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);
    }

    private static async Task<List<AlertItem>> AlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AlertItem>>(ApiRoutes.Alerts))!;

    [Fact]
    public async Task OpenStocktake_AlertsEveryone_UntilClosed()
    {
        var location = await (await Admin.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = "TEST-LOC-1" }))
            .Content.ReadFromJsonAsync<StorageLocationItem>();
        Assert.DoesNotContain(await AlertsAsync(Staff), a => a.Key == AlertKeys.Stocktake);

        var stocktake = await (await Admin.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location!.Id }))
            .Content.ReadFromJsonAsync<StocktakeDetail>();

        var alert = Assert.Single(await AlertsAsync(Staff), a => a.Key == AlertKeys.Stocktake);
        Assert.Contains("TEST-LOC-1", alert.Detail);

        await Admin.PostAsJsonAsync(ApiRoutes.StocktakeClose(stocktake!.Id), new VersionRequest { Version = stocktake.Version });
        Assert.DoesNotContain(await AlertsAsync(Staff), a => a.Key == AlertKeys.Stocktake);
    }

    [Fact]
    public async Task BackupAlert_OnlyForBackupPermission_AndClearsAfterBackup()
    {
        Assert.DoesNotContain(await AlertsAsync(Staff), a => a.Key == AlertKeys.Backup);
        Assert.Contains(await AlertsAsync(Admin), a => a.Key == AlertKeys.Backup);

        (await Admin.GetAsync(ApiRoutes.DataBackup)).EnsureSuccessStatusCode();

        Assert.DoesNotContain(await AlertsAsync(Admin), a => a.Key == AlertKeys.Backup);
    }

    // [تصمیم] آستانه‌ی هشدار پشتیبان یک روز است: ۲۳ ساعت هنوز تازه، ۲۵ ساعت قدیمی.
    [Theory]
    [InlineData(23, false)]
    [InlineData(25, true)]
    public async Task BackupAlert_UsesOneDayThreshold(int hoursOld, bool expectAlert)
    {
        App.WriteBackupMarker(TimeSpan.FromHours(hoursOld));

        var alerts = await AlertsAsync(Admin);

        Assert.Equal(expectAlert, alerts.Any(a => a.Key == AlertKeys.Backup));
    }

    [Fact]
    public async Task Alerts_NeedViewPermission()
    {
        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.Alerts)).StatusCode);
    }

    [Fact]
    public async Task AppInfo_IsPublic_WithProductNameAndVersion()
    {
        using var anonymous = App.NewClient();
        var info = await anonymous.GetFromJsonAsync<AppInfoResponse>(ApiRoutes.AppInfo);

        Assert.Equal(AppInfoResponse.ProductName, info!.Product);
        Assert.StartsWith("0.", info.Version);
        // نام بیمارستان فقط از تنظیمات سرور می‌آید؛ در تست تعریف نشده.
        Assert.Null(info.Organization);
    }

    [Fact]
    public async Task Health_ReportsTheServerVersion()
    {
        var health = await Admin.GetFromJsonAsync<HealthResponse>(ApiRoutes.Health);

        Assert.StartsWith("0.", health!.Version);
    }
}
