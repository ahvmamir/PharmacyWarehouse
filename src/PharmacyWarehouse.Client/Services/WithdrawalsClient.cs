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

    public Task<ApiResult<AllocationResponse>> AllocationAsync(Guid productId, int quantity, int minutesAgo = 0) =>
        ApiSender.SendAsync<AllocationResponse>(ct => _http.GetAsync(ApiRoutes.ProductAllocation(productId, quantity, minutesAgo), ct));

    // ۴۰۹: موجودی/Version عوض شده، بچ منقضی، یا موجودی بچ کم است؛ پیام فارسی در ErrorMessage.
    public Task<ApiResult<WithdrawalItem>> CreateAsync(WithdrawalRequest request) =>
        ApiSender.SendAsync<WithdrawalItem>(ct => _http.PostAsJsonAsync(ApiRoutes.Withdrawals, request, ct));

    public Task<ApiResult<List<WithdrawalItem>>> MineAsync(int take = 50) =>
        ApiSender.SendAsync<List<WithdrawalItem>>(ct => _http.GetAsync($"{ApiRoutes.Withdrawals}?take={take}", ct));

    // status: Pending (پیش‌فرض)، Approved، Rejected یا all.
    public Task<ApiResult<List<WithdrawalItem>>> ReviewListAsync(string status = "Pending") =>
        ApiSender.SendAsync<List<WithdrawalItem>>(ct => _http.GetAsync($"{ApiRoutes.WithdrawalReviewList}?status={Uri.EscapeDataString(status)}", ct));

    public Task<ApiResult<WithdrawalItem>> ApproveAsync(WithdrawalItem item) =>
        ApiSender.SendAsync<WithdrawalItem>(ct => _http.PostAsJsonAsync(ApiRoutes.WithdrawalApprove(item.Id), new ReviewRequest { Version = item.Version }, ct));

    public Task<ApiResult<WithdrawalItem>> RejectAsync(WithdrawalItem item, string note) =>
        ApiSender.SendAsync<WithdrawalItem>(ct => _http.PostAsJsonAsync(ApiRoutes.WithdrawalReject(item.Id), new ReviewRequest { Version = item.Version, Note = note }, ct));

    public Task<ApiResult<BulkApproveResult>> BulkApproveAsync(IEnumerable<WithdrawalItem> items) =>
        ApiSender.SendAsync<BulkApproveResult>(ct => _http.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove,
            new BulkApproveRequest { Items = items.Select(i => new ReviewTarget { Id = i.Id, Version = i.Version }).ToList() }, ct));
}
