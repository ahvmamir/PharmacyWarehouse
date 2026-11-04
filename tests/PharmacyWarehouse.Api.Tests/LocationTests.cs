using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class LocationTests : EquipmentTestBase
{
    [Fact]
    public async Task List_WithoutLogin_Returns401()
    {
        using var client = App.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Units)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Buildings)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Floors)).StatusCode);
    }

    [Fact]
    public async Task Technician_CanRead_ButNotChange()
    {
        Assert.Equal(HttpStatusCode.OK, (await Technician.GetAsync(ApiRoutes.Units)).StatusCode);

        var unit = await Technician.PostAsJsonAsync(ApiRoutes.Units, new SaveUnitRequest { Name = "x" });
        var building = await Technician.PostAsJsonAsync(ApiRoutes.Buildings, new SaveBuildingRequest { Name = "x" });
        var floor = await Technician.PostAsJsonAsync(ApiRoutes.Floors, new SaveFloorRequest { BuildingId = Building.Id, Name = "x" });
        var deactivate = await Technician.PostAsJsonAsync(ApiRoutes.Deactivate(ApiRoutes.Units, UnitA.Id), new VersionRequest { Version = UnitA.Version });

        Assert.Equal(HttpStatusCode.Forbidden, unit.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, building.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, floor.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deactivate.StatusCode);
    }

    [Theory]
    [InlineData("test-unit-a")]
    [InlineData("  TEST-UNIT-A ")]
    [InlineData("test - unit - a")]
    public async Task DuplicateUnitName_AfterNormalizing_Returns409(string name)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Units, new SaveUnitRequest { Name = name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ArabicYehAndKaf_CountAsTheSameName()
    {
        await PostOkAsync<UnitItem>(Admin, ApiRoutes.Units, new SaveUnitRequest { Name = "کیس" });

        var response = await Admin.PostAsJsonAsync(ApiRoutes.Units, new SaveUnitRequest { Name = "كيس" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task EmptyName_Returns400()
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Buildings, new SaveBuildingRequest { Name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SameFloorName_AllowedInAnotherBuilding_RejectedInTheSame()
    {
        var other = await PostOkAsync<BuildingItem>(Admin, ApiRoutes.Buildings, new SaveBuildingRequest { Name = "other" });

        var sameBuilding = await Admin.PostAsJsonAsync(ApiRoutes.Floors, new SaveFloorRequest { BuildingId = Building.Id, Name = "floor-a" });
        var otherBuilding = await Admin.PostAsJsonAsync(ApiRoutes.Floors, new SaveFloorRequest { BuildingId = other.Id, Name = "floor-a" });

        Assert.Equal(HttpStatusCode.Conflict, sameBuilding.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherBuilding.StatusCode);
    }

    [Fact]
    public async Task TwoEditsWithSameVersion_SecondReturns409()
    {
        var route = ApiRoutes.ById(ApiRoutes.Units, UnitA.Id);

        var first = await Admin.PutAsJsonAsync(route, new SaveUnitRequest { Name = "renamed-1", Version = UnitA.Version });
        var second = await Admin.PutAsJsonAsync(route, new SaveUnitRequest { Name = "renamed-2", Version = UnitA.Version });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var saved = await first.Content.ReadFromJsonAsync<UnitItem>();
        Assert.Equal(UnitA.Version + 1, saved!.Version);
    }

    [Fact]
    public async Task DeactivateAndActivate_FlipTheFlag_AndBothAreVersioned()
    {
        var off = await PostOkAsync<UnitItem>(Admin, ApiRoutes.Deactivate(ApiRoutes.Units, UnitA.Id), new VersionRequest { Version = UnitA.Version });
        Assert.False(off.IsActive);

        var on = await PostOkAsync<UnitItem>(Admin, ApiRoutes.Activate(ApiRoutes.Units, UnitA.Id), new VersionRequest { Version = off.Version });
        Assert.True(on.IsActive);
    }

    [Fact]
    public async Task Floors_CanBeFilteredByBuilding_AndAreOrdered()
    {
        var floors = await Admin.GetFromJsonAsync<List<FloorItem>>($"{ApiRoutes.Floors}?buildingId={Building.Id}");

        Assert.Equal(["floor-a", "floor-b"], floors!.Select(f => f.Name));
    }
}
