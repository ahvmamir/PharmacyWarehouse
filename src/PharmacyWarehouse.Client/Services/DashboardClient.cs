using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class DashboardClient
{
    private readonly HttpClient _http;

    public DashboardClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<DashboardResponse>> GetAsync() =>
        ApiSender.SendAsync<DashboardResponse>(ct => _http.GetAsync(ApiRoutes.Dashboard, ct));

    public Task<ApiResult<AppSettingsResponse>> GetSettingsAsync() =>
        ApiSender.SendAsync<AppSettingsResponse>(ct => _http.GetAsync(ApiRoutes.Settings, ct));

    // ۴۰۹: کس دیگری هم‌زمان تنظیم را عوض کرده است؛ پیام فارسی در ErrorMessage.
    public Task<ApiResult<AppSettingsResponse>> SaveSettingsAsync(SaveAppSettingsRequest request) =>
        ApiSender.SendAsync<AppSettingsResponse>(ct => _http.PutAsJsonAsync(ApiRoutes.Settings, request, ct));
}
