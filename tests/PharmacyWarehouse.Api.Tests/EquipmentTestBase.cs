using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// A logged-in admin and technician, plus one building with two floors and two units.
public abstract class EquipmentTestBase : ApiTestBase
{
    protected HttpClient Admin { get; private set; } = null!;
    protected HttpClient Technician { get; private set; } = null!;
    protected BuildingItem Building { get; private set; } = null!;
    protected FloorItem FloorA { get; private set; } = null!;
    protected FloorItem FloorB { get; private set; } = null!;
    protected UnitItem UnitA { get; private set; } = null!;
    protected UnitItem UnitB { get; private set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        await App.AddTechnicianAsync("tech-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        Technician = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        Building = await PostOkAsync<BuildingItem>(Admin, ApiRoutes.Buildings, new SaveBuildingRequest { Name = "test-building" });
        FloorA = await PostOkAsync<FloorItem>(Admin, ApiRoutes.Floors, new SaveFloorRequest { BuildingId = Building.Id, Name = "floor-a" });
        FloorB = await PostOkAsync<FloorItem>(Admin, ApiRoutes.Floors, new SaveFloorRequest { BuildingId = Building.Id, Name = "floor-b", SortOrder = 1 });
        UnitA = await PostOkAsync<UnitItem>(Admin, ApiRoutes.Units, new SaveUnitRequest { Name = "test-unit-a" });
        UnitB = await PostOkAsync<UnitItem>(Admin, ApiRoutes.Units, new SaveUnitRequest { Name = "test-unit-b" });
    }

    protected static async Task<T> PostOkAsync<T>(HttpClient client, string route, object body)
    {
        var response = await client.PostAsync(route, JsonContent.Create(body, body.GetType()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    protected EquipmentInput CaseInput(string? assetNumber = "TEST-0001") => new()
    {
        Type = EquipmentType.Case,
        AssetNumber = assetNumber,
        UseTemporaryAssetNumber = assetNumber is null,
        OwnerUnitId = UnitA.Id,
        BuildingId = Building.Id,
        FloorId = FloorA.Id,
        LocationUnitId = UnitA.Id
    };

    protected EquipmentInput MonitorInput(string? assetNumber = "TEST-0002") => new()
    {
        Type = EquipmentType.Monitor,
        AssetNumber = assetNumber,
        UseTemporaryAssetNumber = assetNumber is null,
        Brand = "TestBrand",
        Model = "TestModel",
        SizeText = "22",
        OwnerUnitId = UnitA.Id,
        BuildingId = Building.Id,
        FloorId = FloorA.Id,
        LocationUnitId = UnitA.Id
    };

    protected Task<EquipmentDetail> CreateAsync(EquipmentInput input) =>
        PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.Equipment, input);

    protected static async Task<EquipmentDetail> GetAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<EquipmentDetail>(ApiRoutes.ById(ApiRoutes.Equipment, id)))!;

    protected static async Task<List<AuditEntryItem>> HistoryAsync(HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<List<AuditEntryItem>>(ApiRoutes.EquipmentHistory(id)))!;

    protected static UpdateEquipmentRequest ToUpdate(EquipmentDetail d) => new()
    {
        Type = d.Type,
        AssetNumber = d.HasTemporaryAssetNumber ? null : d.AssetNumber,
        UseTemporaryAssetNumber = d.HasTemporaryAssetNumber,
        MouseModel = d.MouseModel,
        KeyboardModel = d.KeyboardModel,
        SerialNumber = d.SerialNumber,
        Brand = d.Brand,
        Model = d.Model,
        SizeText = d.SizeText,
        ComputerName = d.ComputerName,
        PeripheralKind = d.PeripheralKind,
        HasMouse = d.HasMouse,
        HasKeyboard = d.HasKeyboard,
        OtherAccessories = d.OtherAccessories,
        OwnerUnitId = d.OwnerUnitId,
        BuildingId = d.BuildingId,
        FloorId = d.FloorId,
        LocationUnitId = d.LocationUnitId,
        Notes = d.Notes,
        Version = d.Version,
        Components = d.Components
            .Select(c => new ComponentInput { Id = c.Id, Kind = c.Kind, Brand = c.Brand, Model = c.Model, Capacity = c.Capacity })
            .ToList()
    };
}
