using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class BulkReceiptTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        await App.AddStaffAsync("staff-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private static ReceiptRequest Line(Guid product, string batch = "TEST-BATCH-01", string? expiry = "15/03/2030", int quantity = 5, int? version = null) =>
        new() { ProductId = product, BatchNumber = batch, Expiry = expiry, Quantity = quantity, BatchVersion = version };

    private Task<HttpResponseMessage> PostAsync(params ReceiptRequest[] lines) =>
        Keeper.PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest { Lines = lines.ToList() });

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Errors;

    private sealed class ProblemBody
    {
        public string? Type { get; set; }
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }

    [Fact]
    public async Task AllValid_SavesEveryLine_AndLedgerIsConsistent()
    {
        var first = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var second = await App.AddProductRawAsync("TEST-DRUG-02", null);

        var response = await PostAsync(Line(first, quantity: 3), Line(second, "TEST-BATCH-02", quantity: 4));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkReceiptResult>())!;
        Assert.Equal([3, 4], result.Lines.Select(l => l.Batch.OnHand));
        Assert.All(result.Lines, l => Assert.True(l.CreatedBatch));
        Assert.Equal(StockMovementKind.Receipt, (await App.MovementsAsync(result.Lines[0].Batch.Id)).Single().Kind);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task OneInvalidLine_SavesNothing_AndReportsThatLine()
    {
        var first = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var second = await App.AddProductRawAsync("TEST-DRUG-02", null);

        var response = await PostAsync(Line(first), Line(second, "TEST-BATCH-02", quantity: 0));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal([BulkReceiptErrors.Key(1, "Quantity")], (await ErrorsAsync(response)).Keys);

        // همه یا هیچ: ردیف سالم هم ثبت نشده است.
        var detail = (await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, first)))!;
        Assert.Empty(detail.Batches);
    }

    [Fact]
    public async Task ReportsEveryBadLine_NotJustTheFirst()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);

        var response = await PostAsync(Line(product, batch: " "), Line(product, "TEST-BATCH-02", expiry: null), Line(Guid.NewGuid(), "TEST-BATCH-03"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Contains(BulkReceiptErrors.Key(0, "BatchNumber"), errors.Keys);
        Assert.Contains(BulkReceiptErrors.Key(1, "Expiry"), errors.Keys);
        Assert.Contains(BulkReceiptErrors.Key(2, "ProductId"), errors.Keys);
    }

    [Fact]
    public async Task StaleBatchVersion_Is409_WithVersionConflictType_AndNothingSaved()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var other = await App.AddProductRawAsync("TEST-DRUG-02", null);
        var created = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Line(product));
        var batch = (await created.Content.ReadFromJsonAsync<ReceiptResult>())!.Batch;

        var response = await PostAsync(Line(other, "TEST-BATCH-02"), Line(product, batch.BatchNumber, expiry: null, version: batch.Version - 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ProblemBody>())!;
        Assert.Equal(ProblemTypes.VersionConflict, body.Type);
        Assert.Contains(BulkReceiptErrors.Key(1, "BatchVersion"), body.Errors.Keys);
        Assert.Single(await App.MovementsAsync(batch.Id));
    }

    [Fact]
    public async Task ExistingBatchWithDifferentDate_Is409()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Line(product));

        var response = await PostAsync(Line(product, expiry: "16/03/2031", version: 1));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(0, "Expiry"), (await ErrorsAsync(response)).Keys);
    }

    [Fact]
    public async Task SameNewBatchInTwoLines_AddsUp_OnOneBatch()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);

        var response = await PostAsync(Line(product, quantity: 2), Line(product, expiry: null, quantity: 3));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkReceiptResult>())!;
        Assert.Equal(result.Lines[0].Batch.Id, result.Lines[1].Batch.Id);
        Assert.Equal(5, result.Lines[1].Batch.OnHand);
        Assert.Equal(2, (await App.MovementsAsync(result.Lines[0].Batch.Id)).Count);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task SameNewBatchWithDifferentDates_Is409()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);

        var response = await PostAsync(Line(product), Line(product, expiry: "16/03/2031"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(1, "Expiry"), (await ErrorsAsync(response)).Keys);
    }

    [Fact]
    public async Task EmptyOrNullLines_Are400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest { Lines = null })).StatusCode);
    }

    [Fact]
    public async Task TooManyLines_Is400()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var lines = Enumerable.Range(0, BulkReceiptRequest.MaxLines + 1).Select(_ => Line(product)).ToArray();

        Assert.Equal(HttpStatusCode.BadRequest, (await PostAsync(lines)).StatusCode);
    }

    [Fact]
    public async Task WithoutPermission_Is403()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);
        var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        var response = await staff.PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest { Lines = [Line(product)] });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousRequest_Is401()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-01", null);

        var response = await App.NewClient().PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest { Lines = [Line(product)] });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
