using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Import;

namespace PharmacyWarehouse.Client.Services;

// ورود و خروج Excel (بخش ۱۲ spec). فایل‌ها بزرگ‌ترند، پس از FileTimeout استفاده می‌شود.
public sealed class ImportClient
{
    private readonly HttpClient _http;

    public ImportClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<ImportPreview>> ParseAsync(byte[] file, string fileName) =>
        ApiSender.SendAsync<ImportPreview>(ct =>
        {
            var content = new MultipartFormDataContent();
            content.Add(new ByteArrayContent(file), "file", fileName);
            return _http.PostAsync(ApiRoutes.ImportParse, content, ct);
        }, ApiSender.FileTimeout);

    public Task<ApiResult<ImportPreview>> ValidateAsync(IEnumerable<ImportRowInput> rows) =>
        ApiSender.SendAsync<ImportPreview>(ct => _http.PostAsJsonAsync(ApiRoutes.ImportValidate, new ImportRequest { Rows = rows.ToList() }, ct), ApiSender.FileTimeout);

    // ۴۰۹ یعنی هنوز خطا مانده یا در این فاصله تغییر کرده؛ پیام فارسی در ErrorMessage.
    public Task<ApiResult<ImportResult>> CommitAsync(IEnumerable<ImportRowInput> rows) =>
        ApiSender.SendAsync<ImportResult>(ct => _http.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows.ToList() }, ct), ApiSender.FileTimeout);

    public Task<ApiResult<DownloadedFile>> TemplateAsync() =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(ApiRoutes.ImportTemplate, ct));

    public Task<ApiResult<DownloadedFile>> ExportAsync() =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(ApiRoutes.DataExport, ct));
}
