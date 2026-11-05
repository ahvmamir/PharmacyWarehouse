using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class BatchAdjustTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private async Task<(Guid Product, Guid Batch)> BatchAsync(int onHand = 10)
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-A1", null);
        return (product, await App.AddBatchRawAsync(product, "TEST-LOT-A1", null, onHand: onHand));
    }

    private async Task<BatchItem> ReadAsync(Guid product, Guid batch)
    {
        var detail = await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, product));
        return Assert.Single(detail!.Batches, b => b.Id == batch);
    }

    [Theory]
    [InlineData(7, -3)]
    [InlineData(12, 2)]
    [InlineData(0, -10)]
    public async Task Adjust_PostsDifference_AndUpdatesOnHand(int counted, int expectedQuantity)
    {
        var (product, batch) = await BatchAsync();
        var before = await ReadAsync(product, batch);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = counted, Note = "TEST شمارش", Version = before.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<BatchItem>())!;
        Assert.Equal(counted, item.OnHand);
        Assert.True(item.Version > before.Version);
        var movement = Assert.Single(await App.MovementsAsync(batch), m => m.Kind == StockMovementKind.Adjustment);
        Assert.Equal(expectedQuantity, movement.Quantity);
        Assert.Equal("TEST شمارش", movement.Note);
        await App.AssertLedgerConsistentAsync();
    }

    [Theory]
    [InlineData(-1, "TEST")]
    [InlineData(5, null)]
    [InlineData(5, "   ")]
    [InlineData(10, "TEST")] // برابر موجودی فعلی
    public async Task Adjust_InvalidInput_Returns400_AndPostsNothing(int counted, string? note)
    {
        var (product, batch) = await BatchAsync();
        var before = await ReadAsync(product, batch);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = counted, Note = note, Version = before.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await App.MovementsAsync(batch));
    }

    [Fact]
    public async Task Adjust_StaleVersion_Returns409()
    {
        var (product, batch) = await BatchAsync();
        var before = await ReadAsync(product, batch);
        Assert.Equal(HttpStatusCode.OK, (await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = 9, Note = "TEST", Version = before.Version })).StatusCode);

        var stale = await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = 8, Note = "TEST", Version = before.Version });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(9, (await ReadAsync(product, batch)).OnHand);
    }

    [Fact]
    public async Task Adjust_UnknownBatch_Returns404()
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(Guid.NewGuid()), new AdjustBatchRequest { Counted = 1, Note = "TEST", Version = 1 });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_PostsNegativeMovement_WithReasonInNote()
    {
        var (product, batch) = await BatchAsync();
        var before = await ReadAsync(product, batch);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchDispose(batch), new DisposeBatchRequest { Quantity = 4, Reason = DisposalReason.Damaged, Note = "TEST شکسته", Version = before.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, (await response.Content.ReadFromJsonAsync<BatchItem>())!.OnHand);
        var movement = Assert.Single(await App.MovementsAsync(batch), m => m.Kind == StockMovementKind.Disposal);
        Assert.Equal(-4, movement.Quantity);
        Assert.Equal(DisposalRules.MovementNote(DisposalReason.Damaged, "TEST شکسته"), movement.Note);
        await App.AssertLedgerConsistentAsync();
    }

    [Theory]
    [InlineData(0, DisposalReason.Expired, "TEST")]
    [InlineData(-1, DisposalReason.Expired, "TEST")]
    [InlineData(11, DisposalReason.Expired, "TEST")]
    [InlineData(1, (DisposalReason)99, "TEST")]
    [InlineData(1, DisposalReason.Other, null)]
    public async Task Dispose_InvalidInput_Returns400_AndPostsNothing(int quantity, DisposalReason reason, string? note)
    {
        var (product, batch) = await BatchAsync();
        var before = await ReadAsync(product, batch);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchDispose(batch), new DisposeBatchRequest { Quantity = quantity, Reason = reason, Note = note, Version = before.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await App.MovementsAsync(batch));
    }

    [Fact]
    public async Task Dispose_StaleVersion_Returns409()
    {
        var (_, batch) = await BatchAsync();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.BatchDispose(batch), new DisposeBatchRequest { Quantity = 1, Reason = DisposalReason.Expired, Note = "TEST", Version = 999 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdjustAndDispose_RequireStockAdjust()
    {
        var (_, batch) = await BatchAsync();
        await App.AddStaffAsync("staff-a");
        await App.AddManagerAsync("manager-a");

        foreach (var client in new[] { await LoggedInAsync("staff-a", TestApp.StaffPassword), await LoggedInAsync("manager-a", TestApp.StaffPassword) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = 1, Note = "TEST", Version = 1 })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.BatchDispose(batch), new DisposeBatchRequest { Quantity = 1, Note = "TEST", Version = 1 })).StatusCode);
        }
    }
}
