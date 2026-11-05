using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class WithdrawalsClient
{
    private readonly HttpClient _http;

    public WithdrawalsClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<AllocationResponse>> AllocationAsync(Guid productId, int quantity) =>
        ApiSender.SendAsync<AllocationResponse>(ct => _http.GetAsync(ApiRoutes.ProductAllocation(productId, quantity), ct));

    // ۴۰۹: موجودی/Version عوض شده، بچ منقضی، یا موجودی بچ کم است؛ پیام فارسی در ErrorMessage.
    public Task<ApiResult<WithdrawalItem>> CreateAsync(WithdrawalRequest request) =>
        ApiSender.SendAsync<WithdrawalItem>(ct => _http.PostAsJsonAsync(ApiRoutes.Withdrawals, request, ct));

    public Task<ApiResult<List<WithdrawalItem>>> MineAsync(int take = 50) =>
        ApiSender.SendAsync<List<WithdrawalItem>>(ct => _http.GetAsync($"{ApiRoutes.Withdrawals}?take={take}", ct));
}
