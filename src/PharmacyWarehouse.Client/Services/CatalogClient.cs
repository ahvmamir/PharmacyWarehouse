using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// ساختمان، طبقه و واحد.
public sealed class CatalogClient
{
    private readonly HttpClient _http;

    public CatalogClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<List<BuildingItem>>> BuildingsAsync() =>
        ApiSender.SendAsync<List<BuildingItem>>(ct => _http.GetAsync(ApiRoutes.Buildings, ct));

    public Task<ApiResult<List<FloorItem>>> FloorsAsync() =>
        ApiSender.SendAsync<List<FloorItem>>(ct => _http.GetAsync(ApiRoutes.Floors, ct));

    public Task<ApiResult<List<UnitItem>>> UnitsAsync() =>
        ApiSender.SendAsync<List<UnitItem>>(ct => _http.GetAsync(ApiRoutes.Units, ct));

    public Task<ApiResult<BuildingItem>> SaveBuildingAsync(Guid? id, SaveBuildingRequest request) =>
        ApiSender.SendAsync<BuildingItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Buildings, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Buildings, request, ct));

    public Task<ApiResult<FloorItem>> SaveFloorAsync(Guid? id, SaveFloorRequest request) =>
        ApiSender.SendAsync<FloorItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Floors, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Floors, request, ct));

    public Task<ApiResult<UnitItem>> SaveUnitAsync(Guid? id, SaveUnitRequest request) =>
        ApiSender.SendAsync<UnitItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Units, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Units, request, ct));

    public Task<ApiResult<BuildingItem>> SetBuildingActiveAsync(BuildingItem item, bool active) =>
        SetActiveAsync<BuildingItem>(ApiRoutes.Buildings, item.Id, item.Version, active);

    public Task<ApiResult<FloorItem>> SetFloorActiveAsync(FloorItem item, bool active) =>
        SetActiveAsync<FloorItem>(ApiRoutes.Floors, item.Id, item.Version, active);

    public Task<ApiResult<UnitItem>> SetUnitActiveAsync(UnitItem item, bool active) =>
        SetActiveAsync<UnitItem>(ApiRoutes.Units, item.Id, item.Version, active);

    public Task<ApiResult<DependencyList>> DependenciesAsync(string collection, Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(collection, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(string collection, Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(collection, id), ct));

    private Task<ApiResult<T>> SetActiveAsync<T>(string collection, Guid id, int version, bool active) where T : class
    {
        var route = active ? ApiRoutes.Activate(collection, id) : ApiRoutes.Deactivate(collection, id);
        return ApiSender.SendAsync<T>(ct => _http.PostAsJsonAsync(route, new VersionRequest { Version = version }, ct));
    }
}
