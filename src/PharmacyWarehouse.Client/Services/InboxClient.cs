using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class InboxClient
{
    private readonly HttpClient _http;

    public InboxClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<InboxCounts>> CountsAsync() =>
        ApiSender.SendAsync<InboxCounts>(ct => _http.GetAsync(ApiRoutes.Inbox, ct));
}
