using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// محل‌های درختی انبار.
public sealed class LocationsClient
{
    private readonly HttpClient _http;

    public LocationsClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<List<StorageLocationItem>>> ListAsync() =>
        ApiSender.SendAsync<List<StorageLocationItem>>(ct => _http.GetAsync(ApiRoutes.Locations, ct));

    public Task<ApiResult<StorageLocationItem>> SaveAsync(Guid? id, SaveStorageLocationRequest request) =>
        ApiSender.SendAsync<StorageLocationItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Locations, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Locations, request, ct));

    public Task<ApiResult<StorageLocationItem>> SetActiveAsync(StorageLocationItem item, bool active)
    {
        var route = active ? ApiRoutes.Activate(ApiRoutes.Locations, item.Id) : ApiRoutes.Deactivate(ApiRoutes.Locations, item.Id);
        return ApiSender.SendAsync<StorageLocationItem>(ct => _http.PostAsJsonAsync(route, new VersionRequest { Version = item.Version }, ct));
    }

    public Task<ApiResult<DependencyList>> DependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Locations, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Locations, id), ct));
}
