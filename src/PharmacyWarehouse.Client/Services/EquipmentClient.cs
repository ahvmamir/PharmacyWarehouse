using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Client.Services;

public sealed class EquipmentQuery
{
    public string? Q { get; set; }
    public EquipmentType? Type { get; set; }
    public EquipmentStatus? Status { get; set; }
    public Guid? OwnerUnitId { get; set; }
    public Guid? BuildingId { get; set; }
    public Guid? FloorId { get; set; }
    public Guid? LocationUnitId { get; set; }
    public bool NoAssetNumber { get; set; }
    public bool IncludeRetired { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public string Sort { get; set; } = EquipmentSort.AssetNumber;
    public bool Descending { get; set; }

    public string ToQueryString()
    {
        var parts = new List<string> { $"page={Page}", $"pageSize={PageSize}" };

        if (!string.IsNullOrWhiteSpace(Q)) parts.Add("q=" + Uri.EscapeDataString(Q));
        if (Type is { } type) parts.Add($"type={type}");
        if (Status is { } status) parts.Add($"status={status}");
        if (OwnerUnitId is { } owner) parts.Add($"ownerUnitId={owner}");
        if (BuildingId is { } building) parts.Add($"buildingId={building}");
        if (FloorId is { } floor) parts.Add($"floorId={floor}");
        if (LocationUnitId is { } location) parts.Add($"locationUnitId={location}");
        if (NoAssetNumber) parts.Add("noAssetNumber=true");
        if (IncludeRetired) parts.Add("includeRetired=true");
        parts.Add($"sort={Sort}");
        if (Descending) parts.Add("desc=true");

        return string.Join('&', parts);
    }
}

public sealed class EquipmentClient
{
    private readonly HttpClient _http;

    public EquipmentClient(HttpClient http)
    {
        _http = http;
    }

    public Task<ApiResult<PagedResult<EquipmentListItem>>> ListAsync(EquipmentQuery query) =>
        ApiSender.SendAsync<PagedResult<EquipmentListItem>>(
            ct => _http.GetAsync($"{ApiRoutes.Equipment}?{query.ToQueryString()}", ct));

    public Task<ApiResult<EquipmentStats>> SummaryAsync() =>
        ApiSender.SendAsync<EquipmentStats>(ct => _http.GetAsync(ApiRoutes.EquipmentSummary, ct));

    public Task<ApiResult<List<EquipmentListItem>>> LookupByAssetNumberAsync(string assetNumber) =>
        ApiSender.SendAsync<List<EquipmentListItem>>(ct => _http.GetAsync(
            $"{ApiRoutes.EquipmentLookup}?assetNumber={Uri.EscapeDataString(assetNumber)}", ct));

    public Task<ApiResult<EquipmentDetail>> GetAsync(Guid id) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.GetAsync(ApiRoutes.ById(ApiRoutes.Equipment, id), ct));

    public Task<ApiResult<EquipmentDetail>> CreateAsync(EquipmentInput input) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.Equipment, input, ct));

    public Task<ApiResult<BulkCreateResponse>> CreateBulkAsync(BulkCreateRequest request) =>
        ApiSender.SendAsync<BulkCreateResponse>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentBulk, request, ct), ApiSender.FileTimeout);

    public Task<ApiResult<EquipmentDetail>> UpdateAsync(Guid id, UpdateEquipmentRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, id), request, ct));

    public Task<ApiResult<EquipmentDetail>> VerifyAsync(Guid id, int version) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentVerify(id), new VerifyRequest { Version = version }, ct));

    public Task<ApiResult<List<RepairItem>>> RepairsAsync(Guid id) =>
        ApiSender.SendAsync<List<RepairItem>>(ct => _http.GetAsync(ApiRoutes.EquipmentRepairs(id), ct));

    public Task<ApiResult<EquipmentDetail>> SendToRepairAsync(Guid id, RepairSendRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentRepairs(id), request, ct));

    public Task<ApiResult<EquipmentDetail>> ReturnFromRepairAsync(Guid repairId, RepairReturnRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.RepairReturn(repairId), request, ct));

    public Task<ApiResult<BulkActionResult>> BulkMoveAsync(BulkMoveRequest request) =>
        ApiSender.SendAsync<BulkActionResult>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentBulkMove, request, ct), ApiSender.FileTimeout);

    public Task<ApiResult<BulkActionResult>> BulkStatusAsync(BulkStatusRequest request) =>
        ApiSender.SendAsync<BulkActionResult>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentBulkStatus, request, ct), ApiSender.FileTimeout);

    public Task<ApiResult<EquipmentDetail>> MoveAsync(Guid id, MoveEquipmentRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentMove(id), request, ct));

    public Task<ApiResult<EquipmentDetail>> ChangeStatusAsync(Guid id, ChangeStatusRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentStatus(id), request, ct));

    public Task<ApiResult<EquipmentDetail>> LinkAsync(Guid id, LinkRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentLink(id), request, ct));

    public Task<ApiResult<EquipmentDetail>> UnlinkAsync(Guid id, LinkRequest request) =>
        ApiSender.SendAsync<EquipmentDetail>(ct => _http.PostAsJsonAsync(ApiRoutes.EquipmentUnlink(id), request, ct));

    public Task<ApiResult<DependencyList>> DependenciesAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.GetAsync(ApiRoutes.Dependencies(ApiRoutes.Equipment, id), ct));

    public Task<ApiResult<DependencyList>> DeleteAsync(Guid id) =>
        ApiSender.SendAsync<DependencyList>(ct => _http.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, id), ct));

    public Task<ApiResult<List<AuditEntryItem>>> HistoryAsync(Guid id) =>
        ApiSender.SendAsync<List<AuditEntryItem>>(ct => _http.GetAsync(ApiRoutes.EquipmentHistory(id), ct));
}
