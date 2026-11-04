using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// نام سازمان و نسخه‌ی سرور، یک بار در هر بارگذاری صفحه.
public sealed class AppInfoState
{
    private readonly HttpClient _http;
    private Task<AppInfoResponse>? _loading;

    public AppInfoState(HttpClient http)
    {
        _http = http;
    }

    public AppInfoResponse? Current { get; private set; }

    public Task<AppInfoResponse> LoadAsync() => _loading ??= FetchAsync();

    private async Task<AppInfoResponse> FetchAsync()
    {
        var result = await ApiSender.SendAsync<AppInfoResponse>(ct => _http.GetAsync(ApiRoutes.AppInfo, ct));
        if (!result.IsSuccess)
        {
            // شبکه قطع بود؛ بار بعد دوباره امتحان شود.
            _loading = null;
            return new AppInfoResponse();
        }

        Current = result.Value!;
        return Current;
    }
}
