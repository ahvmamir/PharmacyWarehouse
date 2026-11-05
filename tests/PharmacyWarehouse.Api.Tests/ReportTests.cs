using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class ReportTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;
    private HttpClient Manager { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        await App.AddManagerAsync("manager-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
        Manager = await LoggedInAsync("manager-a", TestApp.StaffPassword);
    }

    private async Task<(Guid Product, Guid Batch)> BatchAsync(string name = "TEST-DRUG-R1", int onHand = 10)
    {
        var product = await App.AddProductRawAsync(name, null);
        return (product, await App.AddBatchRawAsync(product, $"LOT-{name}", null, onHand: onHand));
    }

    private async Task<WithdrawalItem> WithdrawAsync(Guid product, Guid batch, int quantity)
    {
        var detail = await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, product));
        var version = detail!.Batches.Single(b => b.Id == batch).Version;
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest
        {
            ProductId = product,
            Lines = [new WithdrawalLineRequest { BatchId = batch, Quantity = quantity, BatchVersion = version }]
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
    }

    private async Task<MovementReportResponse> MovementsAsync(string query) =>
        (await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?{query}"))!;

    [Fact]
    public async Task Movements_FilterByKindAndProduct()
    {
        var (a, batchA) = await BatchAsync("TEST-DRUG-R1");
        var (b, _) = await BatchAsync("TEST-DRUG-R2");
        await WithdrawAsync(a, batchA, 2);

        Assert.Equal(3, (await MovementsAsync("days=7")).Total);

        var withdrawals = await MovementsAsync("days=7&kind=Withdrawal");
        var row = Assert.Single(withdrawals.Items);
        Assert.Equal(StockMovementKind.Withdrawal, row.Kind);
        Assert.Equal(-2, row.Quantity);
        Assert.Equal("keeper-a", row.UserName);

        var byProduct = await MovementsAsync($"days=7&productId={b}");
        Assert.Equal(1, byProduct.Total);
        Assert.Equal(b, byProduct.Items[0].ProductId);
    }

    [Fact]
    public async Task Movements_PagesWithSkipAndTake_TotalIsWholeSet()
    {
        await BatchAsync("TEST-DRUG-R1");
        await BatchAsync("TEST-DRUG-R2");
        await BatchAsync("TEST-DRUG-R3");

        var first = await MovementsAsync("days=7&skip=0&take=2");
        var second = await MovementsAsync("days=7&skip=2&take=2");

        Assert.Equal(3, first.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
    }

    [Theory]
    [InlineData("days=0")]
    [InlineData("days=366")]
    [InlineData("skip=-1")]
    [InlineData("kind=Nope")]
    [InlineData("kind=1")]
    public async Task Movements_InvalidQuery_Returns400(string query)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Manager.GetAsync($"{ApiRoutes.ReportMovements}?{query}")).StatusCode);
    }

    [Fact]
    public async Task Consumption_IsNetOfRejectedWithdrawals()
    {
        var (product, batch) = await BatchAsync();
        await WithdrawAsync(product, batch, 4);
        var rejected = await WithdrawAsync(product, batch, 1);
        var reject = await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalReject(rejected.Id), new ReviewRequest { Version = rejected.Version, Note = "TEST برگشت" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var report = await Manager.GetFromJsonAsync<ConsumptionReportResponse>($"{ApiRoutes.ReportConsumption}?days=7");

        Assert.Equal(7, report!.Days);
        Assert.Equal(4, Assert.Single(report.Consumption.TopProducts).Quantity);
        Assert.Equal(4, Assert.Single(report.Consumption.ByUser).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public async Task Consumption_InvalidDays_Returns400(int days)
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Manager.GetAsync($"{ApiRoutes.ReportConsumption}?days={days}")).StatusCode);
    }

    [Fact]
    public async Task Reports_RequireReportsView()
    {
        await App.AddStaffAsync("staff-a");
        var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ApiRoutes.ReportMovements)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ApiRoutes.ReportConsumption)).StatusCode);
    }
}
