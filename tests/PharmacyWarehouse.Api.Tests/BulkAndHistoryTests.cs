using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class BulkAndHistoryTests : EquipmentTestBase
{
    private static BulkItem Item(EquipmentDetail d) => new() { Id = d.Id, Version = d.Version };

    [Fact]
    public async Task LinkAndUnlink_ShowInTheHubsHistory_WithReadableNames()
    {
        var hub = await CreateAsync(CaseInput("H1"));
        var monitor = await CreateAsync(MonitorInput("M1"));

        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id), new LinkRequest { ChildId = monitor.Id, Version = hub.Version })).EnsureSuccessStatusCode();
        hub = await GetAsync(Admin, hub.Id);
        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentUnlink(hub.Id), new LinkRequest { ChildId = monitor.Id, Version = hub.Version })).EnsureSuccessStatusCode();

        var hubHistory = await HistoryAsync(Admin, hub.Id);
        var linkRows = hubHistory.Where(e => e.Field == "ParentId").ToList();
        Assert.Equal(2, linkRows.Count);
        Assert.All(linkRows, r => Assert.Equal("مانیتور M1", r.Subject));
        Assert.Contains(linkRows, r => r.NewValue?.StartsWith(hub.Id.ToString()) == true);

        // در تاریخچه‌ی خود مانیتور، «متصل به» نام کیس است، نه شناسه.
        var monitorRow = (await HistoryAsync(Admin, monitor.Id)).First(e => e.Field == "ParentId" && e.NewValue is not null);
        Assert.Null(monitorRow.Subject);
        Assert.Equal("کیس H1", monitorRow.NewText);
    }

    [Fact]
    public async Task BulkMove_MovesAll_WithLinked_InOneChangeSet()
    {
        var hub = await CreateAsync(CaseInput("B1"));
        var other = await CreateAsync(CaseInput("B2"));
        var monitor = await CreateAsync(MonitorInput("B3"));
        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id), new LinkRequest { ChildId = monitor.Id, Version = hub.Version })).EnsureSuccessStatusCode();
        hub = await GetAsync(Admin, hub.Id);

        var request = new BulkMoveRequest
        {
            Items = [Item(hub), Item(other)], BuildingId = Building.Id, FloorId = FloorB.Id, LocationUnitId = UnitB.Id, Room = "R9", Reason = "unit moved"
        };
        var result = await PostOkAsync<BulkActionResult>(Technician, ApiRoutes.EquipmentBulkMove, request);
        Assert.Equal(2, result.Changed);

        foreach (var id in new[] { hub.Id, other.Id, monitor.Id })
        {
            var device = await GetAsync(Admin, id);
            Assert.Equal(FloorB.Id, device.FloorId);
            Assert.Equal("R9", device.Room);
        }

        var moved = (await HistoryAsync(Admin, hub.Id)).First(e => e.Field == "FloorId");
        var otherMoved = (await HistoryAsync(Admin, other.Id)).First(e => e.Field == "FloorId");
        Assert.Equal(moved.ChangeSetId, otherMoved.ChangeSetId);
        Assert.Equal("unit moved", moved.Reason);
    }

    [Fact]
    public async Task BulkMove_IsAllOrNothing_OnStaleVersionOrRetired()
    {
        var a = await CreateAsync(CaseInput("C1"));
        var b = await CreateAsync(CaseInput("C2"));

        var stale = new BulkMoveRequest { Items = [Item(a), new BulkItem { Id = b.Id, Version = b.Version + 5 }], BuildingId = Building.Id, FloorId = FloorB.Id, LocationUnitId = UnitB.Id };
        Assert.Equal(HttpStatusCode.Conflict, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulkMove, stale)).StatusCode);
        Assert.Equal(FloorA.Id, (await GetAsync(Admin, a.Id)).FloorId);

        (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentStatus(b.Id), new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "old", Version = b.Version })).EnsureSuccessStatusCode();
        b = await GetAsync(Admin, b.Id);
        var withRetired = new BulkMoveRequest { Items = [Item(a), Item(b)], BuildingId = Building.Id, FloorId = FloorB.Id, LocationUnitId = UnitB.Id };
        Assert.Equal(HttpStatusCode.BadRequest, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulkMove, withRetired)).StatusCode);
        Assert.Equal(FloorA.Id, (await GetAsync(Admin, a.Id)).FloorId);
    }

    [Fact]
    public async Task BulkStatus_RetireNeedsReason_AndRestoreNeedsPermission()
    {
        var a = await CreateAsync(CaseInput("S1"));
        var b = await CreateAsync(MonitorInput("S2"));

        var noReason = new BulkStatusRequest { Items = [Item(a), Item(b)], Status = EquipmentStatus.Retired };
        Assert.Equal(HttpStatusCode.BadRequest, (await Admin.PostAsJsonAsync(ApiRoutes.EquipmentBulkStatus, noReason)).StatusCode);

        var retire = new BulkStatusRequest { Items = [Item(a), Item(b)], Status = EquipmentStatus.Retired, Reason = "batch retire" };
        Assert.Equal(2, (await PostOkAsync<BulkActionResult>(Admin, ApiRoutes.EquipmentBulkStatus, retire)).Changed);

        a = await GetAsync(Admin, a.Id);
        b = await GetAsync(Admin, b.Id);
        Assert.Equal(EquipmentStatus.Retired, a.Status);

        // تکنسین به‌طور پیش‌فرض اجازه‌ی بازگرداندن از اسقاط ندارد.
        var restore = new BulkStatusRequest { Items = [Item(a), Item(b)], Status = EquipmentStatus.Active };
        Assert.Equal(HttpStatusCode.Forbidden, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulkStatus, restore)).StatusCode);
    }

    [Fact]
    public async Task Bulk_RejectsEmptyAndTooManyItems()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulkStatus,
            new BulkStatusRequest { Status = EquipmentStatus.InStorage })).StatusCode);

        var many = Enumerable.Range(0, BulkActionRules.MaxItems + 1).Select(_ => new BulkItem { Id = Guid.NewGuid(), Version = 1 }).ToList();
        Assert.Equal(HttpStatusCode.BadRequest, (await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulkStatus,
            new BulkStatusRequest { Items = many, Status = EquipmentStatus.InStorage })).StatusCode);
    }
}
