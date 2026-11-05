using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class StocktakeTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private async Task<Guid> LocationAsync(string name, Guid? parentId = null)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationItem>())!.Id;
    }

    private async Task<(Guid Product, Guid Batch)> BatchAsync(Guid? location, int onHand = 10, string name = "TEST-DRUG-S1", string lot = "TEST-LOT-S1")
    {
        var product = await App.AddProductRawAsync(name, null);
        return (product, await App.AddBatchRawAsync(product, lot, location, onHand: onHand));
    }

    private Task<HttpResponseMessage> StartRawAsync(Guid location) =>
        Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location });

    private async Task<StocktakeDetail> StartAsync(Guid location)
    {
        var response = await StartRawAsync(location);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private Task<HttpResponseMessage> CountRawAsync(Guid stocktake, Guid batch, int counted) =>
        Keeper.PostAsJsonAsync(ApiRoutes.StocktakeCounts(stocktake), new StocktakeCountRequest { BatchId = batch, Counted = counted });

    private async Task<StocktakeDetail> CountAsync(Guid stocktake, Guid batch, int counted)
    {
        var response = await CountRawAsync(stocktake, batch, counted);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private async Task<StocktakeDetail> CloseAsync(StocktakeDetail stocktake)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeClose(stocktake.Id), new VersionRequest { Version = stocktake.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private Task<HttpResponseMessage> ApproveAsync(Guid stocktake, StocktakeCountItem count) =>
        Keeper.PostAsJsonAsync(ApiRoutes.StocktakeApprove(stocktake, count.Id), new VersionRequest { Version = count.Version });

    private async Task<BatchItem> ReadBatchAsync(Guid product, Guid batch)
    {
        var detail = await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, product));
        return Assert.Single(detail!.Batches, b => b.Id == batch);
    }

    private async Task<WithdrawalItem> WithdrawAsync(Guid product, Guid batch, int quantity, int? minutesAgo = null)
    {
        var current = await ReadBatchAsync(product, batch);
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest
        {
            ProductId = product,
            MinutesAgo = minutesAgo,
            Lines = [new WithdrawalLineRequest { BatchId = batch, Quantity = quantity, BatchVersion = current.Version }]
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
    }

    [Fact]
    public async Task Flow_Count_Close_Approve_PostsAdjustmentOnlyAfterApproval()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);

        var counted = await CountAsync(stocktake.Id, batch, 8);

        var count = Assert.Single(counted.Counts);
        Assert.Equal(10, count.ExpectedOnHand);
        Assert.Equal(-2, count.Difference);
        Assert.False(count.IsAdjusted);
        Assert.Equal(10, (await ReadBatchAsync(product, batch)).OnHand);

        var closed = await CloseAsync(counted);
        Assert.NotNull(closed.ClosedAtUtc);
        Assert.Equal(10, (await ReadBatchAsync(product, batch)).OnHand);

        var approved = await ApproveAsync(stocktake.Id, closed.Counts[0]);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var detail = (await approved.Content.ReadFromJsonAsync<StocktakeDetail>())!;
        Assert.True(Assert.Single(detail.Counts).IsAdjusted);
        Assert.Equal(8, (await ReadBatchAsync(product, batch)).OnHand);
        var movement = Assert.Single(await App.MovementsAsync(batch), m => m.Kind == StockMovementKind.Adjustment);
        Assert.Equal(-2, movement.Quantity);
        Assert.Equal(detail.Counts[0].Id, movement.StocktakeCountId);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Approve_WhileOpen_Returns409()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var counted = await CountAsync(stocktake.Id, batch, 8);

        var response = await ApproveAsync(stocktake.Id, counted.Counts[0]);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await App.MovementsAsync(batch));
    }

    [Fact]
    public async Task Approve_Twice_Returns409_AndDoesNotAdjustTwice()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var closed = await CloseAsync(await CountAsync(stocktake.Id, batch, 8));
        var first = await ApproveAsync(stocktake.Id, closed.Counts[0]);
        var fresh = (await first.Content.ReadFromJsonAsync<StocktakeDetail>())!.Counts[0];

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, fresh)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, closed.Counts[0])).StatusCode);

        Assert.Equal(8, (await ReadBatchAsync(product, batch)).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Approve_NoDifference_Returns409()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var closed = await CloseAsync(await CountAsync(stocktake.Id, batch, 10));

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, closed.Counts[0])).StatusCode);
    }

    [Fact]
    public async Task ExpectedOnHand_IsTakenAtCountTime_NotAtStart()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        await WithdrawAsync(product, batch, 3);

        var counted = await CountAsync(stocktake.Id, batch, 7);

        Assert.Equal(7, counted.Counts[0].ExpectedOnHand);
        Assert.Equal(0, counted.Counts[0].Difference);
    }

    [Fact]
    public async Task WithdrawalAfterCount_IsNotCancelledByApproval()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var counted = await CountAsync(stocktake.Id, batch, 8);
        await WithdrawAsync(product, batch, 3);
        var closed = await CloseAsync(counted);

        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(stocktake.Id, closed.Counts[0])).StatusCode);

        Assert.Equal(5, (await ReadBatchAsync(product, batch)).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Approve_ShortageLargerThanCurrentOnHand_Returns409_AndStockStaysValid()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location, onHand: 5);
        var stocktake = await StartAsync(location);
        var counted = await CountAsync(stocktake.Id, batch, 1);
        await WithdrawAsync(product, batch, 3);
        var closed = await CloseAsync(counted);

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, closed.Counts[0])).StatusCode);

        Assert.Equal(2, (await ReadBatchAsync(product, batch)).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Recount_AddsRow_AndLatestIsValid_OldCountCannotBeApproved()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var old = Assert.Single((await CountAsync(stocktake.Id, batch, 8)).Counts);

        var recount = await CountAsync(stocktake.Id, batch, 9);
        var latest = Assert.Single(recount.Counts);
        Assert.NotEqual(old.Id, latest.Id);
        Assert.Equal(9, latest.Counted);

        await CloseAsync(recount);
        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, old)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApproveAsync(stocktake.Id, latest)).StatusCode);
        Assert.Equal(9, (await ReadBatchAsync(product, batch)).OnHand);
    }

    [Fact]
    public async Task NewerCount_InLaterStocktake_InvalidatesOlderCount()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var first = await StartAsync(location);
        var closedFirst = await CloseAsync(await CountAsync(first.Id, batch, 8));
        var second = await StartAsync(location);
        await CountAsync(second.Id, batch, 9);

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(first.Id, closedFirst.Counts[0])).StatusCode);
    }

    [Fact]
    public async Task ManualAdjustment_AfterCount_BlocksApproval()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var counted = await CountAsync(stocktake.Id, batch, 8);
        var current = await ReadBatchAsync(product, batch);
        Assert.Equal(HttpStatusCode.OK, (await Keeper.PostAsJsonAsync(ApiRoutes.BatchAdjust(batch), new AdjustBatchRequest { Counted = 8, Note = "TEST دستی", Version = current.Version })).StatusCode);
        var closed = await CloseAsync(counted);

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(stocktake.Id, closed.Counts[0])).StatusCode);

        Assert.Equal(8, (await ReadBatchAsync(product, batch)).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Start_OverlappingOpenStocktake_Returns409_SiblingAllowed()
    {
        var parent = await LocationAsync("TEST-LOC-P");
        var child = await LocationAsync("TEST-LOC-C", parent);
        var grandchild = await LocationAsync("TEST-LOC-G", child);
        var sibling = await LocationAsync("TEST-LOC-S", parent);

        await StartAsync(child);

        Assert.Equal(HttpStatusCode.Conflict, (await StartRawAsync(child)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StartRawAsync(parent)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await StartRawAsync(grandchild)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await StartRawAsync(sibling)).StatusCode);
    }

    [Fact]
    public async Task Start_AfterClose_IsAllowed_UnknownLocationRejected()
    {
        var location = await LocationAsync("TEST-LOC-1");
        await CloseAsync(await StartAsync(location));

        Assert.Equal(HttpStatusCode.OK, (await StartRawAsync(location)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await StartRawAsync(Guid.NewGuid())).StatusCode);
    }

    [Fact]
    public async Task Count_BatchOutsideScope_OrWithoutLocation_Returns400_SubtreeAllowed()
    {
        var root = await LocationAsync("TEST-LOC-R");
        var child = await LocationAsync("TEST-LOC-C", root);
        var other = await LocationAsync("TEST-LOC-O");
        var (_, inChild) = await BatchAsync(child, name: "TEST-DRUG-1", lot: "TEST-LOT-1");
        var (_, outside) = await BatchAsync(other, name: "TEST-DRUG-2", lot: "TEST-LOT-2");
        var (_, noLocation) = await BatchAsync(null, name: "TEST-DRUG-3", lot: "TEST-LOT-3");
        var stocktake = await StartAsync(root);

        Assert.Equal(HttpStatusCode.OK, (await CountRawAsync(stocktake.Id, inChild, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CountRawAsync(stocktake.Id, outside, 1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CountRawAsync(stocktake.Id, noLocation, 1)).StatusCode);
    }

    [Fact]
    public async Task Batch_InheritsProductLocation_ForScope()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var product = await App.AddProductRawAsync("TEST-DRUG-I", location);
        var batch = await App.AddBatchRawAsync(product, "TEST-LOT-I", null, onHand: 4);
        var stocktake = await StartAsync(location);

        var batches = await Keeper.GetFromJsonAsync<List<StocktakeBatchItem>>(ApiRoutes.StocktakeBatches(stocktake.Id, product));

        Assert.Equal(batch, Assert.Single(batches!).BatchId);
        Assert.Equal(HttpStatusCode.OK, (await CountRawAsync(stocktake.Id, batch, 4)).StatusCode);
    }

    [Fact]
    public async Task Count_NegativeRejected_ClosedRejected_UnknownNotFound()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);

        Assert.Equal(HttpStatusCode.BadRequest, (await CountRawAsync(stocktake.Id, batch, -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CountRawAsync(stocktake.Id, Guid.NewGuid(), 1)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await CountRawAsync(Guid.NewGuid(), batch, 1)).StatusCode);

        await CloseAsync(stocktake);
        Assert.Equal(HttpStatusCode.Conflict, (await CountRawAsync(stocktake.Id, batch, 1)).StatusCode);
    }

    [Fact]
    public async Task Count_DoesNotAdvanceStocktakeVersion_CloseChecksVersion()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);

        var counted = await CountAsync(stocktake.Id, batch, 3);
        Assert.Equal(stocktake.Version, counted.Version);

        var stale = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeClose(stocktake.Id), new VersionRequest { Version = stocktake.Version + 1 });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var closed = await CloseAsync(counted);
        var again = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeClose(stocktake.Id), new VersionRequest { Version = closed.Version });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Missing_ListsUncountedBatchesWithStock()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, counted) = await BatchAsync(location, name: "TEST-DRUG-1", lot: "TEST-LOT-1");
        var (_, uncounted) = await BatchAsync(location, name: "TEST-DRUG-2", lot: "TEST-LOT-2");
        await BatchAsync(location, onHand: 0, name: "TEST-DRUG-3", lot: "TEST-LOT-3");
        var stocktake = await StartAsync(location);
        Assert.Equal(2, stocktake.Missing.Count);

        var detail = await CountAsync(stocktake.Id, counted, 10);

        Assert.Equal(uncounted, Assert.Single(detail.Missing).BatchId);
    }

    [Fact]
    public async Task List_ReportsCountsAndDifferences_OpenFirst()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var other = await LocationAsync("TEST-LOC-2");
        var (_, a) = await BatchAsync(location, name: "TEST-DRUG-1", lot: "TEST-LOT-1");
        var (_, b) = await BatchAsync(location, name: "TEST-DRUG-2", lot: "TEST-LOT-2");
        var closedOne = await StartAsync(location);
        await CountAsync(closedOne.Id, a, 10);
        await CloseAsync(await CountAsync(closedOne.Id, b, 1));
        var openOne = await StartAsync(other);

        var list = (await Keeper.GetFromJsonAsync<List<StocktakeListItem>>(ApiRoutes.Stocktakes))!;

        Assert.Equal([openOne.Id, closedOne.Id], list.Select(s => s.Id));
        Assert.Equal(2, list[1].CountedBatches);
        Assert.Equal(1, list[1].Differences);
    }

    [Fact]
    public async Task AfterStocktakeFlag_SetOnlyWhenWithdrawalOccurredBeforeACount()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (product, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        await CountAsync(stocktake.Id, batch, 10);

        var backdated = await WithdrawAsync(product, batch, 1, minutesAgo: 30);
        var current = await WithdrawAsync(product, batch, 1);

        Assert.True(backdated.AfterStocktake);
        Assert.False(current.AfterStocktake);
    }

    [Fact]
    public async Task Stocktake_And_Count_AreAudited()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var (_, batch) = await BatchAsync(location);
        var stocktake = await StartAsync(location);
        var counted = await CountAsync(stocktake.Id, batch, 5);

        Assert.True(await App.CountAuditAsync(stocktake.Id, "Stocktake") > 0);
        Assert.True(await App.CountAuditAsync(counted.Counts[0].Id, "StocktakeCount") > 0);
    }

    [Fact]
    public async Task Endpoints_RequireStockAdjust()
    {
        var location = await LocationAsync("TEST-LOC-1");
        var stocktake = await StartAsync(location);
        await App.AddStaffAsync("staff-a");
        await App.AddManagerAsync("manager-a");

        foreach (var client in new[] { await LoggedInAsync("staff-a", TestApp.StaffPassword), await LoggedInAsync("manager-a", TestApp.StaffPassword) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.Stocktakes)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.ById(ApiRoutes.Stocktakes, stocktake.Id))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.StocktakeClose(stocktake.Id), new VersionRequest { Version = stocktake.Version })).StatusCode);
        }
    }
}
