using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// فهرست مرجع TTAC (بخش ۱۵.۴ spec). بارگذاری فایل بزرگ است، پس از FileTimeout استفاده می‌شود.
public sealed class CatalogClient
{
    private readonly HttpClient _http;

    public CatalogClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<CatalogImportResult>> ImportAsync(byte[] file, string fileName) =>
        ApiSender.SendAsync<CatalogImportResult>(ct =>
        {
            var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(file), "file", fileName);
            return _http.PostAsync(ApiRoutes.CatalogImport, content, ct);
        }, ApiSender.FileTimeout);

    public Task<ApiResult<CatalogStatus>> StatusAsync() =>
        ApiSender.SendAsync<CatalogStatus>(ct => _http.GetAsync(ApiRoutes.CatalogStatus, ct));

    public Task<ApiResult<List<CatalogSuggestion>>> SearchAsync(string q) =>
        ApiSender.SendAsync<List<CatalogSuggestion>>(ct => _http.GetAsync(ApiRoutes.CatalogSearch(q), ct));
}
