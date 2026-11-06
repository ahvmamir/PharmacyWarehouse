using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

// ورود جنس ساده توسط پرسنل و بررسی آن توسط انباردار (بخش ۱۵.۲ spec).
public sealed class ReceiptsClient
{
    private readonly HttpClient _http;

    public ReceiptsClient(HttpClient http)
    {
        _http = http;
    }

    // خطای ردیف‌ها با کلید «Lines[i].Field» در Problem.Errors است (BulkReceiptErrors).
    public Task<ApiResult<ReceiptItem>> SubmitAsync(QuickReceiptRequest request) =>
        ApiSender.SendAsync<ReceiptItem>(ct => _http.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, request, ct));

    public Task<ApiResult<List<ReceiptItem>>> MineAsync(int take = 50) =>
        ApiSender.SendAsync<List<ReceiptItem>>(ct => _http.GetAsync($"{ApiRoutes.ReceiptsMine}?take={take}", ct));

    public Task<ApiResult<List<ReceiptItem>>> PendingAsync(int take = 100) =>
        ApiSender.SendAsync<List<ReceiptItem>>(ct => _http.GetAsync($"{ApiRoutes.ReceiptsPending}?take={take}", ct));

    public Task<ApiResult<ReceiptItem>> GetAsync(Guid id) =>
        ApiSender.SendAsync<ReceiptItem>(ct => _http.GetAsync(ApiRoutes.ReceiptById(id), ct));

    public Task<ApiResult<ReceiptItem>> SaveReviewAsync(ReceiptItem receipt, List<ReviewLineEdit> edits) =>
        ApiSender.SendAsync<ReceiptItem>(ct => _http.PostAsJsonAsync(ApiRoutes.ReceiptReview(receipt.Id), new ReviewReceiptRequest { Version = receipt.Version, Lines = edits }, ct));

    public Task<ApiResult<ReceiptItem>> ApproveAsync(ReceiptItem receipt) =>
        ApiSender.SendAsync<ReceiptItem>(ct => _http.PostAsJsonAsync(ApiRoutes.ReceiptApprove(receipt.Id), new ReviewRequest { Version = receipt.Version }, ct));

    public Task<ApiResult<ReceiptItem>> RejectAsync(ReceiptItem receipt, string note) =>
        ApiSender.SendAsync<ReceiptItem>(ct => _http.PostAsJsonAsync(ApiRoutes.ReceiptReject(receipt.Id), new ReviewRequest { Version = receipt.Version, Note = note }, ct));
}
