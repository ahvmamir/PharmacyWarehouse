using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// دسته‌های قلم (بخش ۱۵.۳ spec).
public sealed class CategoriesClient
{
    private readonly HttpClient _http;

    public CategoriesClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<List<CategoryItem>>> ListAsync() =>
        ApiSender.SendAsync<List<CategoryItem>>(ct => _http.GetAsync(ApiRoutes.Categories, ct));

    public Task<ApiResult<CategoryItem>> SaveAsync(Guid? id, SaveCategoryRequest request) =>
        ApiSender.SendAsync<CategoryItem>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Categories, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Categories, request, ct));

    public Task<ApiResult<BulkCategoryResult>> AssignAsync(BulkCategoryRequest request) =>
        ApiSender.SendAsync<BulkCategoryResult>(ct => _http.PostAsJsonAsync(ApiRoutes.ProductsBulkCategory, request, ct));

    public Task<ApiResult<DependencyList>> DependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Categories, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Categories, id), ct));
}
