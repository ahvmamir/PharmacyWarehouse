using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class AlertsClient
{
    private readonly HttpClient _http;

    public AlertsClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<List<AlertItem>>> ListAsync() =>
        ApiSender.SendAsync<List<AlertItem>>(ct => _http.GetAsync(ApiRoutes.Alerts, ct));

    public Task<ApiResult<HealthResponse>> HealthAsync() =>
        ApiSender.SendAsync<HealthResponse>(ct => _http.GetAsync(ApiRoutes.Health, ct));
}
