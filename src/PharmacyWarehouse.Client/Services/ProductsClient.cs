using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// اقلام و بارکدهایشان. هر تغییر، ProductDetail تازه را برمی‌گرداند (بارکد هم Version قلم را بالا می‌برد).
public sealed class ProductsClient
{
    private readonly HttpClient _http;

    public ProductsClient(HttpClient http)
    {
        _http = http;
    }

    // filter: zero، belowMin، expiring یا خالی.
    public Task<ApiResult<List<ProductListItem>>> ListAsync(string? q = null, Guid? locationId = null, string? filter = null, bool includeInactive = false)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(q)) query.Add($"q={Uri.EscapeDataString(q)}");
        if (locationId is { } location) query.Add($"locationId={location}");
        if (!string.IsNullOrEmpty(filter)) query.Add($"filter={Uri.EscapeDataString(filter)}");
        if (includeInactive) query.Add("includeInactive=true");
        var url = query.Count == 0 ? ApiRoutes.Products : $"{ApiRoutes.Products}?{string.Join('&', query)}";
        return ApiSender.SendAsync<List<ProductListItem>>(ct => _http.GetAsync(url, ct));
    }

    public Task<ApiResult<ProductDetail>> GetAsync(Guid id) =>
        ApiSender.SendAsync<ProductDetail>(ct => _http.GetAsync(ApiRoutes.ById(ApiRoutes.Products, id), ct));

    // متن خام اسکن؛ سرور ScanKey را اعمال می‌کند. ۴۰۴ یعنی بارکد ناشناخته.
    public Task<ApiResult<ProductDetail>> ByCodeAsync(string code) =>
        ApiSender.SendAsync<ProductDetail>(ct => _http.GetAsync(ApiRoutes.ProductByCode(code), ct));

    public Task<ApiResult<ProductHistory>> HistoryAsync(Guid id) =>
        ApiSender.SendAsync<ProductHistory>(ct => _http.GetAsync(ApiRoutes.ProductHistory(id), ct));

    public Task<ApiResult<ProductDetail>> SaveAsync(Guid? id, SaveProductRequest request) =>
        ApiSender.SendAsync<ProductDetail>(ct => id is { } existing
            ? _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Products, existing), request, ct)
            : _http.PostAsJsonAsync(ApiRoutes.Products, request, ct));

    public Task<ApiResult<ProductDetail>> SetActiveAsync(ProductListItem item, bool active)
    {
        var route = active ? ApiRoutes.Activate(ApiRoutes.Products, item.Id) : ApiRoutes.Deactivate(ApiRoutes.Products, item.Id);
        return ApiSender.SendAsync<ProductDetail>(ct => _http.PostAsJsonAsync(route, new VersionRequest { Version = item.Version }, ct));
    }

    public Task<ApiResult<ProductDetail>> AddBarcodeAsync(Guid id, string code) =>
        ApiSender.SendAsync<ProductDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.ProductBarcodes(id), new AddBarcodeRequest { Code = code }, ct));

    public Task<ApiResult<ProductDetail>> RemoveBarcodeAsync(Guid id, Guid barcodeId) =>
        ApiSender.SendAsync<ProductDetail>(ct => _http.DeleteAsync(ApiRoutes.ProductBarcode(id, barcodeId), ct));

    public Task<ApiResult<DependencyList>> DependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Products, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Products, id), ct));
}
