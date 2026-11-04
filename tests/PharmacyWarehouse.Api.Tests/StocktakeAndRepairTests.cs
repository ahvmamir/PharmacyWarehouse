using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class StocktakeAndRepairTests : EquipmentTestBase
{
    [Fact]
    public async Task Verify_RecordsTime_InHistory_WithoutTouchingLastChange()
    {
        var device = await CreateAsync(CaseInput("V1"));

        var verified = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentVerify(device.Id), new VerifyRequest { Version = device.Version });

        Assert.NotNull(verified.LastVerifiedAtUtc);
        Assert.Equal(device.UpdatedAtUtc, verified.UpdatedAtUtc);
        Assert.Contains(await HistoryAsync(Admin, device.Id), e => e.Field == "LastVerifiedAtUtc" && e.UserName == "tech-a");

        // Version کهنه یعنی کسی هم‌زمان تغییرش داده است.
        Assert.Equal(HttpStatusCode.Conflict, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentVerify(device.Id), new VerifyRequest { Version = device.Version })).StatusCode);
    }

    [Fact]
    public async Task NotVerifiedReport_ListsUnseenDevices()
    {
        var seen = await CreateAsync(CaseInput("NV1"));
        await CreateAsync(CaseInput("NV2"));
        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentVerify(seen.Id), new VerifyRequest { Version = seen.Version });

        var table = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.NotVerified, "days=30"));

        Assert.Contains(table!.Rows, r => r[0] == "NV2" && r[^1] == "هرگز");
        Assert.DoesNotContain(table.Rows, r => r[0] == "NV1");
    }

    [Fact]
    public async Task Repair_SendAndReturn_ChangeStatus_AndKeepHistory()
    {
        var device = await CreateAsync(CaseInput("R1"));

        var sent = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentRepairs(device.Id),
            new RepairSendRequest { Vendor = "TestVendor", Problem = "no power", EquipmentVersion = device.Version });
        Assert.Equal(EquipmentStatus.InRepair, sent.Status);

        // تعمیر باز دوم پذیرفته نیست.
        var again = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentRepairs(device.Id),
            new RepairSendRequest { Problem = "again", EquipmentVersion = sent.Version });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        var repair = Assert.Single((await Technician.GetFromJsonAsync<List<RepairItem>>(ApiRoutes.EquipmentRepairs(device.Id)))!);
        Assert.Null(repair.ReturnedAtUtc);

        var back = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.RepairReturn(repair.Id), new RepairReturnRequest
        {
            Result = "PSU replaced", CostRials = 1_500_000, NewStatus = EquipmentStatus.Active, Version = repair.Version, EquipmentVersion = sent.Version
        });
        Assert.Equal(EquipmentStatus.Active, back.Status);

        repair = Assert.Single((await Technician.GetFromJsonAsync<List<RepairItem>>(ApiRoutes.EquipmentRepairs(device.Id)))!);
        Assert.NotNull(repair.ReturnedAtUtc);
        Assert.Equal(1_500_000, repair.CostRials);

        var history = await HistoryAsync(Admin, device.Id);
        Assert.Contains(history, e => e.EntityType == "RepairRecord" && e.Action == "Created");
        Assert.Contains(history, e => e.EntityType == "RepairRecord" && e.Field == "Result");

        var report = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.Repairs, "days=30"));
        Assert.Contains(report!.Rows, r => r[0] == "R1" && r[5] == "TestVendor");
    }

    [Fact]
    public async Task Repair_ReturnOnlyToActiveOrStorage_AndBlocksDelete()
    {
        var device = await CreateAsync(MonitorInput("R2"));
        var sent = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentRepairs(device.Id),
            new RepairSendRequest { Problem = "lines on screen", EquipmentVersion = device.Version });
        var repair = Assert.Single((await Technician.GetFromJsonAsync<List<RepairItem>>(ApiRoutes.EquipmentRepairs(device.Id)))!);

        var retire = await Technician.PostAsJsonAsync(ApiRoutes.RepairReturn(repair.Id), new RepairReturnRequest
        {
            NewStatus = EquipmentStatus.Retired, Version = repair.Version, EquipmentVersion = sent.Version
        });
        Assert.Equal(HttpStatusCode.BadRequest, retire.StatusCode);

        // سابقه‌ی تعمیر مثل تاریخچه است: دستگاه حذف نمی‌شود.
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id))).StatusCode);
    }

    [Fact]
    public async Task VerifyAndRepair_NeedPermissions()
    {
        var device = await CreateAsync(CaseInput("P1"));
        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.EquipmentVerify(device.Id), new VerifyRequest { Version = device.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.EquipmentRepairs(device.Id),
            new RepairSendRequest { Problem = "x", EquipmentVersion = device.Version })).StatusCode);
    }
}
