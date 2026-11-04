using System.Net.Http.Headers;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// گزارش‌ها، فایل‌های Excel و بکاپ.
public sealed class ReportsClient
{
    private readonly HttpClient _http;

    public ReportsClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<ReportTable>> GetAsync(string key, string query) =>
        ApiSender.SendAsync<ReportTable>(ct => _http.GetAsync(ApiRoutes.Report(key, query), ct), ApiSender.FileTimeout);

    public Task<ApiResult<DownloadedFile>> ExcelAsync(string key, string query) =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(ApiRoutes.ReportExcel(key, query), ct));

    public Task<ApiResult<List<ChangeSetItem>>> ChangeSetsAsync(int days) =>
        ApiSender.SendAsync<List<ChangeSetItem>>(ct => _http.GetAsync($"{ApiRoutes.RecentChangeSets}?days={days}", ct), ApiSender.FileTimeout);

    // نوع‌ها با کاما، مثل «Case,Monitor»؛ خالی یعنی همه.
    private static string TypesQuery(IEnumerable<EquipmentType> types) =>
        Uri.EscapeDataString(string.Join(',', types));

    public Task<ApiResult<DownloadedFile>> TemplateAsync(IEnumerable<EquipmentType> types) =>
        ApiSender.DownloadAsync(ct => _http.GetAsync($"{ApiRoutes.DataTemplate}?types={TypesQuery(types)}", ct));

    public Task<ApiResult<DownloadedFile>> ExportAllAsync() =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(ApiRoutes.DataExport, ct));

    public Task<ApiResult<DownloadedFile>> BackupAsync() =>
        ApiSender.DownloadAsync(ct => _http.GetAsync(ApiRoutes.DataBackup, ct));

    public Task<ApiResult<ImportResult>> CheckRowsAsync(ImportRowsRequest request) =>
        ApiSender.SendAsync<ImportResult>(ct => _http.PostAsJsonAsync(ApiRoutes.DataImportCheck, request, ct), ApiSender.FileTimeout);

    public Task<ApiResult<ImportResult>> CommitRowsAsync(ImportRowsRequest request) =>
        ApiSender.SendAsync<ImportResult>(ct => _http.PostAsJsonAsync(ApiRoutes.DataImportCommitRows, request, ct), ApiSender.FileTimeout);

    public Task<ApiResult<ImportResult>> ImportAsync(byte[] file, string fileName, bool createMissing, bool commit, IEnumerable<EquipmentType> types)
    {
        var route = commit ? ApiRoutes.DataImportCommit : ApiRoutes.DataImportPreview;

        return ApiSender.SendAsync<ImportResult>(ct =>
        {
            var content = new MultipartFormDataContent();
            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(part, "file", fileName);
            return _http.PostAsync($"{route}?createMissing={(createMissing ? "true" : "false")}&types={TypesQuery(types)}", content, ct);
        }, ApiSender.FileTimeout);
    }
}

public static class RoleText
{
    public static string Label(string? role) => role switch
    {
        null or "" => "بدون نقش",
        PharmacyWarehouse.Domain.Auth.Roles.Admin => "مدیر",
        PharmacyWarehouse.Domain.Auth.Roles.Technician => "تکنسین",
        _ => role
    };
}
