using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Client.Services;

// اصلاح و اسقاط بچ، انبارگردانی، گزارش‌ها و برچسب (برش ۶). ۴۰۹ یعنی Version یا موجودی عوض شده و پیام فارسی در ErrorMessage است.
public sealed class StockClient
{
    private readonly HttpClient _http;

    public StockClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<BatchItem>> AdjustAsync(Guid batchId, AdjustBatchRequest request) =>
        ApiSender.SendAsync<BatchItem>(ct => _http.PostAsJsonAsync(ApiRoutes.BatchAdjust(batchId), request, ct));

    public Task<ApiResult<BatchItem>> DisposeAsync(Guid batchId, DisposeBatchRequest request) =>
        ApiSender.SendAsync<BatchItem>(ct => _http.PostAsJsonAsync(ApiRoutes.BatchDispose(batchId), request, ct));

    public Task<ApiResult<List<StocktakeListItem>>> StocktakesAsync() =>
        ApiSender.SendAsync<List<StocktakeListItem>>(ct => _http.GetAsync(ApiRoutes.Stocktakes, ct));

    public Task<ApiResult<StocktakeDetail>> StartStocktakeAsync(Guid locationId) =>
        ApiSender.SendAsync<StocktakeDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = locationId }, ct));

    public Task<ApiResult<StocktakeDetail>> StocktakeAsync(Guid id) =>
        ApiSender.SendAsync<StocktakeDetail>(ct => _http.GetAsync(ApiRoutes.ById(ApiRoutes.Stocktakes, id), ct));

    public Task<ApiResult<List<StocktakeBatchItem>>> StocktakeBatchesAsync(Guid id, Guid productId) =>
        ApiSender.SendAsync<List<StocktakeBatchItem>>(ct => _http.GetAsync(ApiRoutes.StocktakeBatches(id, productId), ct));

    public Task<ApiResult<StocktakeDetail>> CountAsync(Guid id, Guid batchId, int counted) =>
        ApiSender.SendAsync<StocktakeDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.StocktakeCounts(id), new StocktakeCountRequest { BatchId = batchId, Counted = counted }, ct));

    public Task<ApiResult<StocktakeDetail>> CloseStocktakeAsync(Guid id, int version) =>
        ApiSender.SendAsync<StocktakeDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.StocktakeClose(id), new VersionRequest { Version = version }, ct));

    public Task<ApiResult<StocktakeDetail>> ApproveCountAsync(Guid id, Guid countId, int version) =>
        ApiSender.SendAsync<StocktakeDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.StocktakeApprove(id, countId), new VersionRequest { Version = version }, ct));

    public Task<ApiResult<MovementReportResponse>> MovementReportAsync(int days, StockMovementKind? kind, Guid? productId, int skip, int take)
    {
        var query = new List<string> { $"days={days}", $"skip={skip}", $"take={take}" };
        if (kind is { } k) query.Add($"kind={k}");
        if (productId is { } id) query.Add($"productId={id}");
        var url = $"{ApiRoutes.ReportMovements}?{string.Join('&', query)}";
        return ApiSender.SendAsync<MovementReportResponse>(ct => _http.GetAsync(url, ct));
    }

    public Task<ApiResult<ConsumptionReportResponse>> ConsumptionReportAsync(int days) =>
        ApiSender.SendAsync<ConsumptionReportResponse>(ct => _http.GetAsync($"{ApiRoutes.ReportConsumption}?days={days}", ct));

    public Task<ApiResult<List<ProductLabelItem>>> LabelProductsAsync() =>
        ApiSender.SendAsync<List<ProductLabelItem>>(ct => _http.GetAsync(ApiRoutes.LabelProducts, ct));

    public Task<ApiResult<List<ProductLabelItem>>> GenerateLabelsAsync(IReadOnlyCollection<Guid> productIds) =>
        ApiSender.SendAsync<List<ProductLabelItem>>(ct => _http.PostAsJsonAsync(ApiRoutes.LabelProductsGenerate, new GenerateLabelsRequest { ProductIds = productIds.ToList() }, ct));
}
