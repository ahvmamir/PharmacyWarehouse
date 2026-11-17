using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

// جای نادرست، بچ تازه و منقضی در انبارگردانی (بخش ۱۵.۳ spec).
public class StocktakeMisplacedTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-m");
        Keeper = await LoggedInAsync("keeper-m", TestApp.StaffPassword);
    }

    private async Task<Guid> LocationAsync(string name) =>
        (await (await Keeper.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = name }))
            .Content.ReadFromJsonAsync<StorageLocationItem>())!.Id;

    private async Task<PeriodDetail> PeriodAsync(string name, params Guid[] locations)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakePeriods, new SavePeriodRequest { Name = name, LocationIds = [.. locations] });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PeriodDetail>())!;
    }

    private async Task<StocktakeDetail> StartAsync(Guid location, Guid period)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location, PeriodId = period });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private async Task<StocktakeDetail> CloseAsync(Guid session)
    {
        var current = (await Keeper.GetFromJsonAsync<StocktakeDetail>(ApiRoutes.ById(ApiRoutes.Stocktakes, session)))!;
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeClose(session), new VersionRequest { Version = current.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private Task<HttpResponseMessage> MisplacedRawAsync(Guid session, Guid batch, int counted) =>
        Keeper.PostAsJsonAsync(ApiRoutes.StocktakeMisplaced(session), new MisplacedCountRequest { BatchId = batch, Counted = counted });

    private async Task<StocktakeDetail> MisplacedAsync(Guid session, Guid batch, int counted)
    {
        var response = await MisplacedRawAsync(session, batch, counted);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private async Task<HttpResponseMessage> ResolveRawAsync(MisplacedItem item, bool relocate) =>
        await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeMisplacedResolve(item.StocktakeId, item.Id), new ResolveMisplacedRequest { Relocate = relocate, Version = item.Version });

    private async Task CountAsync(Guid session, Guid batch, int counted) =>
        (await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeCounts(session), new StocktakeCountRequest { BatchId = batch, Counted = counted })).EnsureSuccessStatusCode();

    private async Task<PeriodDetail> GetPeriodAsync(Guid id) => (await Keeper.GetFromJsonAsync<PeriodDetail>(ApiRoutes.ById(ApiRoutes.StocktakePeriods, id)))!;

    private Task<HttpResponseMessage> ClosePeriodRawAsync(PeriodDetail period) =>
        Keeper.PostAsJsonAsync(ApiRoutes.PeriodClose(period.Id), new VersionRequest { Version = period.Version });

    private async Task<Batch> BatchRowAsync(Guid batch)
    {
        using var scope = App.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Batches.AsNoTracking().SingleAsync(b => b.Id == batch);
    }

    [Fact]
    public async Task Misplaced_IsListed_NotADiscrepancy_AndBlocksPeriodCloseUntilResolved()
    {
        var home = await LocationAsync("TEST-LOC-M1");
        var other = await LocationAsync("TEST-LOC-M2");
        var product = await App.AddProductRawAsync("TEST-DRUG-M1", home);
        var batch = await App.AddBatchRawAsync(product, "TEST-BM1", null, onHand: 10);
        var period = await PeriodAsync("TEST-PERIOD-M1", home, other);
        var session = await StartAsync(other, period.Id);

        var batches = (await Keeper.GetFromJsonAsync<List<StocktakeBatchItem>>(ApiRoutes.StocktakeBatches(session.Id, product)))!;
        Assert.False(Assert.Single(batches).InScope);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeCounts(session.Id), new StocktakeCountRequest { BatchId = batch, Counted = 3 })).StatusCode);

        var detail = await MisplacedAsync(session.Id, batch, 3);
        var found = Assert.Single(detail.Misplaced);
        Assert.Equal("TEST-LOC-M1", found.HomePath);
        Assert.Empty(detail.Counts);
        await CloseAsync(session.Id);

        var stats = await GetPeriodAsync(period.Id);
        Assert.Equal(1, stats.Summary.OpenMisplaced);
        Assert.Equal(0, stats.Summary.Discrepancy);
        Assert.Equal(HttpStatusCode.Conflict, (await ClosePeriodRawAsync(stats)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await ResolveRawAsync(Assert.Single(stats.Misplaced), relocate: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ResolveRawAsync(Assert.Single(stats.Misplaced), relocate: false)).StatusCode);

        stats = await GetPeriodAsync(period.Id);
        Assert.Equal(0, stats.Summary.OpenMisplaced);
        Assert.Equal(HttpStatusCode.OK, (await ClosePeriodRawAsync(stats)).StatusCode);
        Assert.Equal(10, (await BatchRowAsync(batch)).OnHand);
        Assert.Null((await BatchRowAsync(batch)).LocationId);
    }

    [Fact]
    public async Task MisplacedAfterHomeCount_RequiresRecount()
    {
        var home = await LocationAsync("TEST-LOC-M3");
        var other = await LocationAsync("TEST-LOC-M4");
        var product = await App.AddProductRawAsync("TEST-DRUG-M2", home);
        var batch = await App.AddBatchRawAsync(product, "TEST-BM2", null, onHand: 10);
        var period = await PeriodAsync("TEST-PERIOD-M2", home, other);

        var first = await StartAsync(home, period.Id);
        await CountAsync(first.Id, batch, 7);
        await CloseAsync(first.Id);
        var away = await StartAsync(other, period.Id);
        await MisplacedAsync(away.Id, batch, 3);
        await CloseAsync(away.Id);

        var stats = await GetPeriodAsync(period.Id);
        var row = Assert.Single(stats.Rows);
        Assert.True(row.NeedsRecount);
        Assert.Equal(1, stats.Summary.Recount);

        var resolve = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value }]
        });
        Assert.Single((await resolve.Content.ReadFromJsonAsync<ResolveCountsResult>())!.Failed);
        Assert.Equal(10, (await BatchRowAsync(batch)).OnHand);

        Assert.Equal(HttpStatusCode.OK, (await ResolveRawAsync(Assert.Single(stats.Misplaced), relocate: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ClosePeriodRawAsync(await GetPeriodAsync(period.Id))).StatusCode);

        var recount = await StartAsync(home, period.Id);
        await CountAsync(recount.Id, batch, 10);
        await CloseAsync(recount.Id);

        stats = await GetPeriodAsync(period.Id);
        row = Assert.Single(stats.Rows);
        Assert.False(row.NeedsRecount);
        Assert.Equal(PeriodRowStatus.Matched, row.Status);
        Assert.Equal(HttpStatusCode.OK, (await ClosePeriodRawAsync(stats)).StatusCode);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Relocate_MovesBatchToFoundLocation()
    {
        var home = await LocationAsync("TEST-LOC-M5");
        var other = await LocationAsync("TEST-LOC-M6");
        var product = await App.AddProductRawAsync("TEST-DRUG-M3", home);
        var batch = await App.AddBatchRawAsync(product, "TEST-BM3", null, onHand: 2);
        var period = await PeriodAsync("TEST-PERIOD-M3", other);
        var session = await StartAsync(other, period.Id);

        var found = Assert.Single((await MisplacedAsync(session.Id, batch, 2)).Misplaced);
        Assert.Equal(HttpStatusCode.OK, (await ResolveRawAsync(found, relocate: true)).StatusCode);

        Assert.Equal(other, (await BatchRowAsync(batch)).LocationId);
        // حالا جایش همین محل است و شمارش عادی می‌گیرد.
        await CountAsync(session.Id, batch, 2);
    }

    [Fact]
    public async Task Misplaced_RejectsBatchThatBelongsHere_AndZeroCount()
    {
        var home = await LocationAsync("TEST-LOC-M7");
        var other = await LocationAsync("TEST-LOC-M8");
        var product = await App.AddProductRawAsync("TEST-DRUG-M4", home);
        var batch = await App.AddBatchRawAsync(product, "TEST-BM4", null, onHand: 2);
        var period = await PeriodAsync("TEST-PERIOD-M4", home, other);
        var here = await StartAsync(home, period.Id);
        var away = await StartAsync(other, period.Id);

        Assert.Equal(HttpStatusCode.BadRequest, (await MisplacedRawAsync(here.Id, batch, 2)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await MisplacedRawAsync(away.Id, batch, 0)).StatusCode);
    }

    [Fact]
    public async Task NewBatch_WithPastExpiry_CountsFromZero_AndAdjustsUp()
    {
        var home = await LocationAsync("TEST-LOC-M9");
        var product = await App.AddProductRawAsync("TEST-DRUG-M5", home);
        var period = await PeriodAsync("TEST-PERIOD-M5", home);
        var session = await StartAsync(home, period.Id);

        var request = new NewBatchCountRequest { ProductId = product, BatchNumber = "TEST-OLD-1", Expiry = "01/2020", Counted = 4 };
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeNewBatch(session.Id), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var count = Assert.Single((await response.Content.ReadFromJsonAsync<StocktakeDetail>())!.Counts);
        Assert.Equal(0, count.ExpectedOnHand);
        Assert.Equal(HttpStatusCode.Conflict, (await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeNewBatch(session.Id), request)).StatusCode);

        var batch = await BatchRowAsync(count.BatchId);
        Assert.Equal(0, batch.OnHand);
        Assert.Null(batch.LocationId);

        await CloseAsync(session.Id);
        var row = Assert.Single((await GetPeriodAsync(period.Id)).Rows);
        Assert.True(row.IsExpired);
        var resolve = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value }]
        });
        Assert.Single((await resolve.Content.ReadFromJsonAsync<ResolveCountsResult>())!.Done);
        Assert.Equal(4, (await BatchRowAsync(count.BatchId)).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task NewBatch_ProductHomeElsewhere_GetsSessionLocation()
    {
        var home = await LocationAsync("TEST-LOC-M10");
        var other = await LocationAsync("TEST-LOC-M11");
        var product = await App.AddProductRawAsync("TEST-DRUG-M6", home);
        var period = await PeriodAsync("TEST-PERIOD-M6", other);
        var session = await StartAsync(other, period.Id);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeNewBatch(session.Id),
            new NewBatchCountRequest { ProductId = product, BatchNumber = "TEST-OLD-2", Expiry = "01/2030", Counted = 1 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var count = Assert.Single((await response.Content.ReadFromJsonAsync<StocktakeDetail>())!.Counts);
        Assert.Equal(other, (await BatchRowAsync(count.BatchId)).LocationId);
    }

    [Fact]
    public async Task Batches_FlagExpired()
    {
        var home = await LocationAsync("TEST-LOC-M12");
        var product = await App.AddProductRawAsync("TEST-DRUG-M7", home);
        await App.AddBatchRawAsync(product, "TEST-BM7-OLD", null, onHand: 1, expiry: new DateOnly(2020, 1, 31));
        await App.AddBatchRawAsync(product, "TEST-BM7-NEW", null, onHand: 1, expiry: new DateOnly(2099, 1, 31));
        var period = await PeriodAsync("TEST-PERIOD-M7", home);
        var session = await StartAsync(home, period.Id);

        var batches = (await Keeper.GetFromJsonAsync<List<StocktakeBatchItem>>(ApiRoutes.StocktakeBatches(session.Id, product)))!;

        Assert.True(batches.Single(b => b.BatchNumber == "TEST-BM7-OLD").IsExpired);
        Assert.False(batches.Single(b => b.BatchNumber == "TEST-BM7-NEW").IsExpired);
    }
}
