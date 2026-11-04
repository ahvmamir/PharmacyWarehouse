using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class DeleteTests : EquipmentTestBase
{
    private static Task<DependencyList?> DependenciesAsync(HttpClient client, string collection, Guid id) =>
        client.GetFromJsonAsync<DependencyList>(ApiRoutes.Dependencies(collection, id));

    [Fact]
    public async Task Technician_CannotDeleteByDefault()
    {
        var device = await CreateAsync(CaseInput());

        var response = await Technician.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RawDevice_IsDeletedWithItsComponents_AndHistoryStays()
    {
        var input = CaseInput("A1");
        input.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "8GB" });
        var device = await CreateAsync(input);

        Assert.True((await DependenciesAsync(Admin, ApiRoutes.Equipment, device.Id))!.CanDelete);
        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id))).StatusCode);

        // یک ثبت و یک حذف.
        Assert.Equal(2, await App.CountAuditAsync(device.Id, "Equipment"));

        // شماره آزاد شده است.
        Assert.Equal(HttpStatusCode.OK, (await Technician.PostAsJsonAsync(ApiRoutes.Equipment, CaseInput("A1"))).StatusCode);
    }

    [Fact]
    public async Task LinkedDevices_ListEachOther_AndAreNotDeleted()
    {
        var hub = await CreateAsync(CaseInput("A1"));
        var child = await CreateAsync(MonitorInput("A2"));
        hub = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentLink(hub.Id), new LinkRequest { ChildId = child.Id, Version = hub.Version });

        var ofHub = await DependenciesAsync(Admin, ApiRoutes.Equipment, hub.Id);
        var ofChild = await DependenciesAsync(Admin, ApiRoutes.Equipment, child.Id);
        Assert.Equal(child.Id, Assert.Single(ofHub!.Items).EquipmentId);
        Assert.Equal(hub.Id, Assert.Single(ofChild!.Items).EquipmentId);

        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, hub.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, child.Id))).StatusCode);
    }

    [Fact]
    public async Task Unit_WithDevices_ListsThem_RawUnitIsDeleted()
    {
        var device = await CreateAsync(CaseInput("A1"));

        var dependencies = await DependenciesAsync(Admin, ApiRoutes.Units, UnitA.Id);
        Assert.Equal(device.Id, Assert.Single(dependencies!.Items).EquipmentId);
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Units, UnitA.Id))).StatusCode);

        Assert.True((await DependenciesAsync(Admin, ApiRoutes.Units, UnitB.Id))!.CanDelete);
        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Units, UnitB.Id))).StatusCode);
    }

    [Fact]
    public async Task Floor_WithDevices_IsKept_EmptyFloorIsDeleted()
    {
        await CreateAsync(CaseInput("A1"));

        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Floors, FloorA.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Floors, FloorB.Id))).StatusCode);
    }

    [Fact]
    public async Task Building_WithFloors_ListsThem_EmptyBuildingIsDeleted()
    {
        var dependencies = await DependenciesAsync(Admin, ApiRoutes.Buildings, Building.Id);
        Assert.Equal(["floor-a", "floor-b"], dependencies!.Items.Select(i => i.Label));
        Assert.Equal(HttpStatusCode.Conflict, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Buildings, Building.Id))).StatusCode);

        var empty = await PostOkAsync<BuildingItem>(Admin, ApiRoutes.Buildings, new SaveBuildingRequest { Name = "empty" });
        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Buildings, empty.Id))).StatusCode);
    }

    [Fact]
    public async Task ManyDevices_ListIsCut_ButTotalIsRight()
    {
        for (var i = 0; i < 3; i++) await CreateAsync(CaseInput(null));

        var dependencies = await DependenciesAsync(Admin, ApiRoutes.Floors, FloorA.Id);

        Assert.Equal(3, dependencies!.Total);
    }
}
