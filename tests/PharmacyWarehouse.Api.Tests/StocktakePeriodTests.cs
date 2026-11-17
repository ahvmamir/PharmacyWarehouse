using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

// دوره‌ی انبارگردانی (بخش ۱۵.۳ spec): نوبت زیر دوره، دامنه‌ی محل و دسته، آمار، رفع اختلاف در سطح دوره و بستن.
public class StocktakePeriodTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-p");
        Keeper = await LoggedInAsync("keeper-p", TestApp.StaffPassword);
    }

    private async Task<Guid> LocationAsync(string name) =>
        (await (await Keeper.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = name }))
            .Content.ReadFromJsonAsync<StorageLocationItem>())!.Id;

    private async Task<HttpResponseMessage> CreatePeriodRawAsync(string name, Guid[]? locations = null, Guid[]? categories = null) =>
        await Keeper.PostAsJsonAsync(ApiRoutes.StocktakePeriods, new SavePeriodRequest
        {
            Name = name, LocationIds = [.. locations ?? []], CategoryIds = [.. categories ?? []]
        });

    private async Task<PeriodDetail> CreatePeriodAsync(string name, Guid[]? locations = null, Guid[]? categories = null)
    {
        var response = await CreatePeriodRawAsync(name, locations, categories);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PeriodDetail>())!;
    }

    private async Task<StocktakeDetail> StartAsync(Guid location, Guid period, Guid? category = null)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location, PeriodId = period, CategoryId = category });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private async Task<StocktakeDetail> CountAndCloseAsync(StocktakeDetail session, Guid batch, int counted)
    {
        (await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeCounts(session.Id), new StocktakeCountRequest { BatchId = batch, Counted = counted })).EnsureSuccessStatusCode();
        var closed = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeClose(session.Id), new VersionRequest { Version = session.Version });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        return (await closed.Content.ReadFromJsonAsync<StocktakeDetail>())!;
    }

    private async Task<PeriodDetail> GetPeriodAsync(Guid id) => (await Keeper.GetFromJsonAsync<PeriodDetail>(ApiRoutes.ById(ApiRoutes.StocktakePeriods, id)))!;

    private async Task<int> OnHandAsync(Guid batch)
    {
        using var scope = App.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Batches.Where(b => b.Id == batch).Select(b => b.OnHand).SingleAsync();
    }

    [Fact]
    public async Task StartWithoutPeriod_IsValidationError()
    {
        var location = await LocationAsync("TEST-LOC-P1");

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SessionLocation_MustBeInsidePeriodScope()
    {
        var inside = await LocationAsync("TEST-LOC-P2");
        var outside = await LocationAsync("TEST-LOC-P3");
        var period = await CreatePeriodAsync("TEST-PERIOD-A", [inside]);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = outside, PeriodId = period.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await StartAsync(inside, period.Id);
    }

    [Fact]
    public async Task OverlappingOpenPeriods_Conflict_DisjointOnesDoNot()
    {
        var a = await LocationAsync("TEST-LOC-P4");
        var b = await LocationAsync("TEST-LOC-P5");
        await CreatePeriodAsync("TEST-PERIOD-B", [a]);

        Assert.Equal(HttpStatusCode.Conflict, (await CreatePeriodRawAsync("TEST-PERIOD-C", [a])).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await CreatePeriodRawAsync("TEST-PERIOD-D")).StatusCode); // «همه» با a هم‌پوشان است
        Assert.Equal(HttpStatusCode.OK, (await CreatePeriodRawAsync("TEST-PERIOD-E", [b])).StatusCode);
    }

    [Fact]
    public async Task Stats_ShowUncounted_ThenDiscrepancy_AndAdjustFixesStock()
    {
        var location = await LocationAsync("TEST-LOC-P6");
        var product = await App.AddProductRawAsync("TEST-DRUG-P1", location);
        var batch = await App.AddBatchRawAsync(product, "TEST-BP1", null, onHand: 5);
        var period = await CreatePeriodAsync("TEST-PERIOD-F", [location]);

        var row = Assert.Single(period.Rows);
        Assert.Equal(PeriodRowStatus.NotCounted, row.Status);

        var session = await StartAsync(location, period.Id);
        await CountAndCloseAsync(session, batch, 3);

        var detail = await GetPeriodAsync(period.Id);
        row = Assert.Single(detail.Rows);
        Assert.Equal(PeriodRowStatus.Discrepancy, row.Status);
        Assert.Equal(-2, row.Difference);
        Assert.Equal(1, detail.Summary.Discrepancy);

        var resolve = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value }]
        });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        Assert.Single((await resolve.Content.ReadFromJsonAsync<ResolveCountsResult>())!.Done);

        Assert.Equal(3, await OnHandAsync(batch));
        Assert.Equal(PeriodRowStatus.Adjusted, Assert.Single((await GetPeriodAsync(period.Id)).Rows).Status);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Dismiss_NeedsNote_AndKeepsStock()
    {
        var location = await LocationAsync("TEST-LOC-P7");
        var product = await App.AddProductRawAsync("TEST-DRUG-P2", location);
        var batch = await App.AddBatchRawAsync(product, "TEST-BP2", null, onHand: 4);
        var period = await CreatePeriodAsync("TEST-PERIOD-G", [location]);
        await CountAndCloseAsync(await StartAsync(location, period.Id), batch, 9);
        var row = Assert.Single((await GetPeriodAsync(period.Id)).Rows);

        var noNote = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value, Dismiss = true }]
        });
        Assert.Single((await noNote.Content.ReadFromJsonAsync<ResolveCountsResult>())!.Failed);

        var withNote = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value, Dismiss = true, Note = "TEST خطای شمارش" }]
        });
        Assert.Single((await withNote.Content.ReadFromJsonAsync<ResolveCountsResult>())!.Done);

        Assert.Equal(4, await OnHandAsync(batch));
        var after = Assert.Single((await GetPeriodAsync(period.Id)).Rows);
        Assert.Equal(PeriodRowStatus.Dismissed, after.Status);
        Assert.Equal("TEST خطای شمارش", after.DismissNote);
    }

    [Fact]
    public async Task Close_BlockedByOpenSessionOrDiscrepancy_ThenClosedPeriodRejectsSessions()
    {
        var location = await LocationAsync("TEST-LOC-P8");
        var product = await App.AddProductRawAsync("TEST-DRUG-P3", location);
        var batch = await App.AddBatchRawAsync(product, "TEST-BP3", null, onHand: 2);
        var period = await CreatePeriodAsync("TEST-PERIOD-H", [location]);
        var session = await StartAsync(location, period.Id);

        var whileOpen = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodClose(period.Id), new VersionRequest { Version = period.Version });
        Assert.Equal(HttpStatusCode.Conflict, whileOpen.StatusCode);

        await CountAndCloseAsync(session, batch, 1);
        var withDiscrepancy = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodClose(period.Id), new VersionRequest { Version = period.Version });
        Assert.Equal(HttpStatusCode.Conflict, withDiscrepancy.StatusCode);

        var row = Assert.Single((await GetPeriodAsync(period.Id)).Rows);
        await Keeper.PostAsJsonAsync(ApiRoutes.PeriodResolve(period.Id), new ResolveCountsRequest
        {
            Items = [new ResolveCountItem { CountId = row.CountId!.Value, Version = row.CountVersion!.Value }]
        });
        var closed = await Keeper.PostAsJsonAsync(ApiRoutes.PeriodClose(period.Id), new VersionRequest { Version = period.Version });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        var late = await Keeper.PostAsJsonAsync(ApiRoutes.Stocktakes, new StartStocktakeRequest { LocationId = location, PeriodId = period.Id });
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task UpdateScope_CannotDropLocationWithSession_ButCanAdd()
    {
        var a = await LocationAsync("TEST-LOC-P9");
        var b = await LocationAsync("TEST-LOC-P10");
        var period = await CreatePeriodAsync("TEST-PERIOD-I", [a]);
        await StartAsync(a, period.Id);

        var drop = await Keeper.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.StocktakePeriods, period.Id),
            new SavePeriodRequest { Name = period.Name, LocationIds = [b], Version = period.Version });
        Assert.Equal(HttpStatusCode.Conflict, drop.StatusCode);

        var add = await Keeper.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.StocktakePeriods, period.Id),
            new SavePeriodRequest { Name = period.Name, LocationIds = [a, b], Version = period.Version });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        Assert.Equal(2, (await add.Content.ReadFromJsonAsync<PeriodDetail>())!.LocationIds.Count);
    }

    [Fact]
    public async Task CategoryScope_ExcludesOtherProducts_AndRejectsTheirCounts()
    {
        var location = await LocationAsync("TEST-LOC-P11");
        var category = (await (await Keeper.PostAsJsonAsync(ApiRoutes.Categories, new SaveCategoryRequest { Name = "TEST-CAT-P" }))
            .Content.ReadFromJsonAsync<CategoryItem>())!;
        var inCategory = await App.AddProductRawAsync("TEST-DRUG-P4", location);
        var other = await App.AddProductRawAsync("TEST-DRUG-P5", location);
        await App.AddBatchRawAsync(inCategory, "TEST-BP4", null, onHand: 1);
        var otherBatch = await App.AddBatchRawAsync(other, "TEST-BP5", null, onHand: 1);
        var detail = (await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, inCategory)))!;
        (await Keeper.PostAsJsonAsync(ApiRoutes.ProductsBulkCategory, new BulkCategoryRequest
        {
            CategoryId = category.Id,
            Items = [new ReviewTarget { Id = inCategory, Version = detail.Summary.Version }]
        })).EnsureSuccessStatusCode();

        var period = await CreatePeriodAsync("TEST-PERIOD-J", [location], [category.Id]);
        Assert.Equal("TEST-DRUG-P4", Assert.Single(period.Rows).ProductName);

        var session = await StartAsync(location, period.Id);
        var count = await Keeper.PostAsJsonAsync(ApiRoutes.StocktakeCounts(session.Id), new StocktakeCountRequest { BatchId = otherBatch, Counted = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, count.StatusCode);
    }
}
