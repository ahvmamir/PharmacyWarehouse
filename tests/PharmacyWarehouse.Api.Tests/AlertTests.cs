using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class AlertTests : EquipmentTestBase
{
    private static async Task<List<AlertItem>> AlertsAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<List<AlertItem>>(ApiRoutes.Alerts))!;

    private EquipmentInput Named(string asset, string computerName)
    {
        var input = CaseInput(asset);
        input.ComputerName = computerName;
        return input;
    }

    [Fact]
    public async Task DuplicateComputerNames_AreReported_IgnoringCaseAndRetired()
    {
        await CreateAsync(Named("D1", "pc-dup"));
        await CreateAsync(Named("D2", "PC-DUP"));
        await CreateAsync(Named("D3", "PC-OTHER"));
        var retired = await CreateAsync(Named("D4", "PC-OTHER"));
        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentStatus(retired.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = retired.Version })).EnsureSuccessStatusCode();

        var alert = Assert.Single(await AlertsAsync(Technician), a => a.Key == AlertKeys.DuplicateComputerNames);

        Assert.Equal(1, alert.Count);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal(2, alert.Entries.Count);
        Assert.All(alert.Entries, e => Assert.NotNull(e.EquipmentId));
    }

    [Fact]
    public async Task NoProblems_NoEquipmentAlerts()
    {
        await CreateAsync(Named("N1", "PC-1"));
        await CreateAsync(Named("N2", "PC-2"));

        var alerts = await AlertsAsync(Technician);

        Assert.DoesNotContain(alerts, a => a.Key is AlertKeys.DuplicateComputerNames or AlertKeys.TemporaryAssets or AlertKeys.LongRepair);
    }

    [Fact]
    public async Task TemporaryAssetNumbers_AreReported()
    {
        await CreateAsync(CaseInput(null));

        var alert = Assert.Single(await AlertsAsync(Technician), a => a.Key == AlertKeys.TemporaryAssets);
        Assert.Equal(1, alert.Count);
    }

    [Fact]
    public async Task BackupAlert_OnlyForBackupPermission_AndClearsAfterBackup()
    {
        Assert.DoesNotContain(await AlertsAsync(Technician), a => a.Key == AlertKeys.Backup);
        Assert.Contains(await AlertsAsync(Admin), a => a.Key == AlertKeys.Backup);

        (await Admin.GetAsync(ApiRoutes.DataBackup)).EnsureSuccessStatusCode();

        Assert.DoesNotContain(await AlertsAsync(Admin), a => a.Key == AlertKeys.Backup);
    }

    [Fact]
    public async Task DataQuality_DuplicateSerials_PlaceMismatch_AndBareCases()
    {
        var serialA = CaseInput("Q1"); serialA.SerialNumber = "sn-dup";
        var serialB = MonitorInput("Q2"); serialB.SerialNumber = "SN-DUP";
        var hub = await CreateAsync(serialA);
        var monitor = await CreateAsync(serialB);

        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id), new LinkRequest { ChildId = monitor.Id, Version = hub.Version })).EnsureSuccessStatusCode();
        monitor = await GetAsync(Admin, monitor.Id);
        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentMove(monitor.Id), new MoveEquipmentRequest
        {
            BuildingId = Building.Id, FloorId = FloorB.Id, LocationUnitId = UnitB.Id, MoveLinked = false, Version = monitor.Version
        })).EnsureSuccessStatusCode();

        var alerts = await AlertsAsync(Technician);

        Assert.Equal(1, Assert.Single(alerts, a => a.Key == AlertKeys.DuplicateSerials).Count);
        Assert.Equal(monitor.Id, Assert.Single(Assert.Single(alerts, a => a.Key == AlertKeys.LinkedPlaceMismatch).Entries).EquipmentId);
        Assert.Equal(hub.Id, Assert.Single(Assert.Single(alerts, a => a.Key == AlertKeys.HubWithoutComponents).Entries).EquipmentId);
    }

    [Fact]
    public async Task Alerts_NeedViewPermission()
    {
        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.TechnicianPassword);

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
