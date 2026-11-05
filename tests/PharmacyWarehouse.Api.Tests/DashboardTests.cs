using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Stock;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Tests;

public class DashboardTests : ApiTestBase
{
    private HttpClient Staff { get; set; } = null!;
    private HttpClient Keeper { get; set; } = null!;
    private HttpClient Manager { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddStaffAsync("staff-a");
        await App.AddKeeperAsync("keeper-a");
        await App.AddManagerAsync("manager-a");
        Staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
        Manager = await LoggedInAsync("manager-a", TestApp.StaffPassword);
    }

    private static DateOnly Today => LocalDay.Today(DateTime.UtcNow);

    private Task<DashboardResponse?> DashboardAsync(HttpClient? client = null) =>
        (client ?? Manager).GetFromJsonAsync<DashboardResponse>(ApiRoutes.Dashboard);

    private async Task<WithdrawalItem> WithdrawAsync(Guid product, int quantity)
    {
        var allocation = (await Staff.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, quantity)))!;
        var response = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest
        {
            ProductId = product,
            Lines = allocation.Lines.Select(l => new WithdrawalLineRequest { BatchId = l.BatchId, Quantity = l.Quantity, BatchVersion = l.Version }).ToList()
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
    }

    private async Task SetMinStockAsync(Guid product, int? min)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        (await db.Products.FirstAsync(p => p.Id == product)).MinStock = min;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Dashboard_RequiresDashboardView()
    {
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.GetAsync(ApiRoutes.Dashboard)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Keeper.GetAsync(ApiRoutes.Dashboard)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.NewClient().GetAsync(ApiRoutes.Dashboard)).StatusCode);
    }

    [Fact]
    public async Task Expiry_BucketsOnlyBatchesWithStock_AndUsesTheThresholds()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی انقضا", null);
        await App.AddBatchRawAsync(product, "TEST-EXP", null, onHand: 2, expiry: Today.AddDays(-1));
        await App.AddBatchRawAsync(product, "TEST-EMPTY", null, onHand: 0, expiry: Today.AddDays(-1));
        await App.AddBatchRawAsync(product, "TEST-NEAR", null, onHand: 2, expiry: Today.AddMonths(2));
        await App.AddBatchRawAsync(product, "TEST-FAR", null, onHand: 2, expiry: Today.AddMonths(5));
        await App.AddBatchRawAsync(product, "TEST-LATER", null, onHand: 2, expiry: Today.AddMonths(9));
        await App.AddBatchRawAsync(product, "TEST-NONE", null, onHand: 2);

        var expiry = (await DashboardAsync())!.Expiry;

        Assert.Equal((3, 6), (expiry.NearMonths, expiry.FarMonths));
        Assert.Equal("TEST-EXP", Assert.Single(expiry.Expired.Items).BatchNumber);
        Assert.Equal("TEST-NEAR", Assert.Single(expiry.Near.Items).BatchNumber);
        Assert.Equal("TEST-FAR", Assert.Single(expiry.Far.Items).BatchNumber);
        Assert.Equal(1, expiry.Far.Total);
    }

    [Fact]
    public async Task BelowMinimum_CountsOnlyUnexpiredStock_IncludesZeroStock_SkipsEmptyMinAndInactive()
    {
        var expiredOnly = await App.AddProductRawAsync("داروی آزمایشی منقضی", null);
        await App.AddBatchRawAsync(expiredOnly, "TEST-X1", null, onHand: 50, expiry: Today.AddDays(-1));
        await SetMinStockAsync(expiredOnly, 5);

        var zero = await App.AddProductRawAsync("داروی آزمایشی صفر", null);
        await SetMinStockAsync(zero, 1);

        var enough = await App.AddProductRawAsync("داروی آزمایشی کافی", null);
        await App.AddBatchRawAsync(enough, "TEST-X2", null, onHand: 5, expiry: Today.AddDays(30));
        await SetMinStockAsync(enough, 5);

        var noMin = await App.AddProductRawAsync("داروی آزمایشی بی‌حداقل", null);

        var inactive = await App.AddProductRawAsync("داروی آزمایشی غیرفعال", null);
        await SetMinStockAsync(inactive, 5);
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FirstAsync(p => p.Id == inactive)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var below = (await DashboardAsync())!.BelowMinimum;

        Assert.Equal(new[] { expiredOnly, zero }.Order(), below.Select(b => b.ProductId).Order());
        Assert.Equal(0, below.Single(b => b.ProductId == expiredOnly).UnexpiredOnHand);
        Assert.DoesNotContain(below, b => b.ProductId == noMin);
    }

    [Fact]
    public async Task Consumption_IsNetOfRejections_ByProductAndUser()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی مصرف", null);
        await App.AddBatchRawAsync(product, "TEST-C1", null, onHand: 20, expiry: Today.AddDays(200));
        await WithdrawAsync(product, 5);
        var rejected = await WithdrawAsync(product, 3);
        var reject = await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalReject(rejected.Id), new ReviewRequest { Version = rejected.Version, Note = "آزمایشی" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var dashboard = (await DashboardAsync())!;

        Assert.Equal(5, Assert.Single(dashboard.Consumption7Days.TopProducts).Quantity);
        Assert.Equal(5, Assert.Single(dashboard.Consumption30Days.TopProducts).Quantity);
        var user = Assert.Single(dashboard.Consumption30Days.ByUser);
        Assert.Equal("staff-a", user.Name);
        Assert.Equal(5, user.Quantity);
    }

    [Fact]
    public async Task Consumption_OldWithdrawalRejectedToday_DoesNotGoNegativeInTheShortWindow()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی قدیمی", null);
        var batchId = await App.AddBatchRawAsync(product, "TEST-C2", null, onHand: 20, expiry: Today.AddDays(200));
        var old = await WithdrawAsync(product, 4);

        // برداشت را ۱۰ روز عقب می‌بریم؛ StockMovement فقط اضافه‌شونده است، پس فقط زمان خود برداشت جابه‌جا می‌شود.
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Withdrawals.FirstAsync(w => w.Id == old.Id)).OccurredAtUtc = DateTime.UtcNow.AddDays(-10);
            await db.SaveChangesAsync();
        }

        var fresh = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList))!.Single(w => w.Id == old.Id);
        Assert.Equal(HttpStatusCode.OK, (await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalReject(old.Id), new ReviewRequest { Version = fresh.Version, Note = "آزمایشی" })).StatusCode);

        var dashboard = (await DashboardAsync())!;

        Assert.Empty(dashboard.Consumption7Days.TopProducts);
        Assert.Empty(dashboard.Consumption30Days.TopProducts);
        Assert.NotEqual(Guid.Empty, batchId);
    }

    [Fact]
    public async Task Unreviewed_CountsPendingOldestSelfReviewedAndLate()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی بررسی", null);
        await App.AddBatchRawAsync(product, "TEST-U1", null, onHand: 20, expiry: Today.AddDays(200));
        var first = await WithdrawAsync(product, 1);
        await WithdrawAsync(product, 1);

        // برداشت خود انباردار و تأیید خودش
        var allocation = (await Keeper.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, 1)))!;
        var own = await Keeper.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest
        {
            ProductId = product,
            Lines = allocation.Lines.Select(l => new WithdrawalLineRequest { BatchId = l.BatchId, Quantity = l.Quantity, BatchVersion = l.Version }).ToList()
        });
        var ownItem = (await own.Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.Equal(HttpStatusCode.OK, (await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalApprove(ownItem.Id), new ReviewRequest { Version = ownItem.Version })).StatusCode);

        var unreviewed = (await DashboardAsync())!.Unreviewed;

        Assert.Equal(2, unreviewed.PendingCount);
        Assert.Equal(first.AtUtc, unreviewed.OldestPendingAtUtc);
        Assert.Equal(1, unreviewed.SelfReviewed30Days);
        Assert.Equal(0, unreviewed.Late30Days);
        Assert.Equal(0, unreviewed.AfterStocktake30Days);
    }

    [Fact]
    public async Task Corrections_ListAdjustmentAndDisposal_NotOtherKinds()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی اصلاح", null);
        var batchId = await App.AddBatchRawAsync(product, "TEST-K1", null, onHand: 10, expiry: Today.AddDays(200));
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            var ledger = scope.ServiceProvider.GetRequiredService<StockLedger>();
            var batch = await db.Batches.FirstAsync(b => b.Id == batchId);
            ledger.Post(batch, StockMovementKind.Adjustment, -2, DateTime.UtcNow, "شمارش آزمایشی");
            ledger.Post(batch, StockMovementKind.Disposal, -1, DateTime.UtcNow, "اسقاط آزمایشی");
            await db.SaveChangesAsync();
        }

        var corrections = (await DashboardAsync())!.Corrections30Days;

        Assert.Equal(2, corrections.Count);
        Assert.Contains(corrections, c => c.Kind == StockMovementKind.Adjustment && c.Quantity == -2 && c.Note == "شمارش آزمایشی" && c.ProductName == "داروی آزمایشی اصلاح");
        Assert.Contains(corrections, c => c.Kind == StockMovementKind.Disposal && c.Quantity == -1);
    }

    [Fact]
    public async Task Settings_DefaultsThreeAndSix_ReadNeedsDashboardView_WriteNeedsSettingsManage()
    {
        var settings = (await Keeper.GetFromJsonAsync<AppSettingsResponse>(ApiRoutes.Settings))!;
        Assert.Equal((3, 6), (settings.NearMonths, settings.FarMonths));
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.GetAsync(ApiRoutes.Settings)).StatusCode);

        var request = new SaveAppSettingsRequest { NearMonths = 2, FarMonths = 4, NearVersion = settings.NearVersion, FarVersion = settings.FarVersion };
        Assert.Equal(HttpStatusCode.Forbidden, (await Keeper.PutAsJsonAsync(ApiRoutes.Settings, request)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Manager.PutAsJsonAsync(ApiRoutes.Settings, request)).StatusCode);

        var expiry = (await DashboardAsync())!.Expiry;
        Assert.Equal((2, 4), (expiry.NearMonths, expiry.FarMonths));
    }

    [Theory]
    [InlineData(6, 3)]
    [InlineData(3, 3)]
    [InlineData(0, 3)]
    [InlineData(3, 61)]
    public async Task Settings_InvalidThresholds_Return400(int near, int far)
    {
        var settings = (await Manager.GetFromJsonAsync<AppSettingsResponse>(ApiRoutes.Settings))!;

        var response = await Manager.PutAsJsonAsync(ApiRoutes.Settings, new SaveAppSettingsRequest { NearMonths = near, FarMonths = far, NearVersion = settings.NearVersion, FarVersion = settings.FarVersion });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Settings_StaleVersion_Returns409_AndChangesNothing()
    {
        var settings = (await Manager.GetFromJsonAsync<AppSettingsResponse>(ApiRoutes.Settings))!;
        var first = new SaveAppSettingsRequest { NearMonths = 2, FarMonths = 6, NearVersion = settings.NearVersion, FarVersion = settings.FarVersion };
        Assert.Equal(HttpStatusCode.OK, (await Manager.PutAsJsonAsync(ApiRoutes.Settings, first)).StatusCode);

        var stale = new SaveAppSettingsRequest { NearMonths = 1, FarMonths = 5, NearVersion = settings.NearVersion, FarVersion = settings.FarVersion };
        Assert.Equal(HttpStatusCode.Conflict, (await Manager.PutAsJsonAsync(ApiRoutes.Settings, stale)).StatusCode);

        var after = (await Manager.GetFromJsonAsync<AppSettingsResponse>(ApiRoutes.Settings))!;
        Assert.Equal((2, 6), (after.NearMonths, after.FarMonths));
    }

    [Fact]
    public async Task Settings_Change_IsAudited()
    {
        var settings = (await Manager.GetFromJsonAsync<AppSettingsResponse>(ApiRoutes.Settings))!;
        Assert.Equal(HttpStatusCode.OK, (await Manager.PutAsJsonAsync(ApiRoutes.Settings,
            new SaveAppSettingsRequest { NearMonths = 2, FarMonths = 6, NearVersion = settings.NearVersion, FarVersion = settings.FarVersion })).StatusCode);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var near = await db.AppSettings.AsNoTracking().FirstAsync(s => s.Key == DashboardRules.NearMonthsKey);
        var row = Assert.Single(await App.AuditRowsAsync(near.Id));
        Assert.Equal("Value", row.Field);
        Assert.Equal(("3", "2"), (row.OldValue, row.NewValue));
    }
}
