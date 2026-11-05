using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Tests;

public class WithdrawalTests : ApiTestBase
{
    private HttpClient Staff { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddStaffAsync("staff-a");
        await App.AddStaffAsync("staff-b");
        await App.AddKeeperAsync("keeper-a");
        await App.AddManagerAsync("manager-a");
        Staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);
    }

    private static DateOnly Today => LocalDay.Today(DateTime.UtcNow);

    private async Task<(Guid Product, Guid Early, Guid Late)> TwoBatchesAsync()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-W1", null);
        var late = await App.AddBatchRawAsync(product, "LOT-LATE", null, onHand: 10, expiry: Today.AddDays(400));
        var early = await App.AddBatchRawAsync(product, "LOT-EARLY", null, onHand: 4, expiry: Today.AddDays(60));
        return (product, early, late);
    }

    private async Task<AllocationResponse> AllocationAsync(HttpClient client, Guid product, int quantity)
    {
        var response = await client.GetAsync(ApiRoutes.ProductAllocation(product, quantity));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AllocationResponse>())!;
    }

    private static WithdrawalRequest Req(Guid product, params (AllocationLineItem Line, int Quantity)[] lines) =>
        new() { ProductId = product, Lines = lines.Select(l => new WithdrawalLineRequest { BatchId = l.Line.BatchId, Quantity = l.Quantity, BatchVersion = l.Line.Version }).ToList() };

    private async Task SetAllowExpiredAsync(Guid product)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        (await db.Products.FirstAsync(p => p.Id == product)).AllowExpiredWithdrawal = true;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Allocation_SplitsFefo_EarliestExpiryFirst()
    {
        var (product, early, late) = await TwoBatchesAsync();

        var result = await AllocationAsync(Staff, product, 6);

        Assert.Equal(0, result.Shortfall);
        Assert.Collection(result.Lines,
            l => { Assert.Equal(early, l.BatchId); Assert.Equal(4, l.Quantity); },
            l => { Assert.Equal(late, l.BatchId); Assert.Equal(2, l.Quantity); });
        Assert.Equal(14, result.UsableOnHand);
        Assert.Equal(2, result.Available.Count);
    }

    [Fact]
    public async Task Allocation_SkipsExpired_ButListsItAsAvailableLast()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-W2", null);
        var expired = await App.AddBatchRawAsync(product, "LOT-OLD", null, onHand: 9, expiry: Today.AddDays(-1));
        var fresh = await App.AddBatchRawAsync(product, "LOT-NEW", null, onHand: 3, expiry: Today.AddDays(30));

        var result = await AllocationAsync(Staff, product, 5);

        Assert.Equal(2, result.Shortfall);
        Assert.Equal(fresh, Assert.Single(result.Lines).BatchId);
        Assert.Equal([fresh, expired], result.Available.Select(a => a.BatchId));
        Assert.True(result.Available[1].IsExpired);
        Assert.Equal(3, result.UsableOnHand);
    }

    [Fact]
    public async Task Allocation_BadQuantity_And_UnknownProduct()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-W3", null);
        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.GetAsync(ApiRoutes.ProductAllocation(product, 0))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Staff.GetAsync(ApiRoutes.ProductAllocation(Guid.NewGuid(), 1))).StatusCode);
    }

    [Fact]
    public async Task Create_SpansBatches_PostsNegativeMovements_AndKeepsLedgerConsistent()
    {
        var (product, early, late) = await TwoBatchesAsync();
        var allocation = await AllocationAsync(Staff, product, 6);

        var response = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (allocation.Lines[0], 4), (allocation.Lines[1], 2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.Equal(WithdrawalStatus.Pending, item.Status);
        Assert.Equal(6, item.TotalQuantity);
        Assert.Equal("staff-a", item.UserName);
        Assert.False(item.IsLate);
        Assert.Equal(2, item.Lines.Count);

        var earlyMovement = Assert.Single(await App.MovementsAsync(early), m => m.Kind == StockMovementKind.Withdrawal);
        Assert.Equal(-4, earlyMovement.Quantity);
        Assert.Equal(item.Id, earlyMovement.WithdrawalId);
        Assert.Equal(8, (await AllocationAsync(Staff, product, 1)).UsableOnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Create_MoreThanOnHand_Returns409_AndChangesNothing()
    {
        var (product, early, _) = await TwoBatchesAsync();
        var allocation = await AllocationAsync(Staff, product, 1);

        var response = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (allocation.Lines[0], 5)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await App.MovementsAsync(early)); // فقط Import
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Create_StaleBatchVersion_Returns409VersionConflict()
    {
        var (product, _, _) = await TwoBatchesAsync();
        var allocation = await AllocationAsync(Staff, product, 1);
        var other = await LoggedInAsync("staff-b", TestApp.StaffPassword);

        // دیگری اول برمی‌دارد؛ Version بچ جلو می‌رود.
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (allocation.Lines[0], 1)))).StatusCode);
        var stale = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (allocation.Lines[0], 1)));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Contains(ProblemTypes.VersionConflict, await stale.Content.ReadAsStringAsync());
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Create_ExpiredBatch_RejectedByDefault_AllowedWhenProductPermits()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-W4", null);
        await App.AddBatchRawAsync(product, "LOT-OLD", null, onHand: 5, expiry: Today.AddDays(-3));
        var expired = (await AllocationAsync(Staff, product, 1)).Available.Single();

        var request = Req(product, (expired, 2));
        Assert.Equal(HttpStatusCode.Conflict, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, request)).StatusCode);

        await SetAllowExpiredAsync(product);
        var allowed = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, request);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.True((await allowed.Content.ReadFromJsonAsync<WithdrawalItem>())!.ContainsExpired);
    }

    [Fact]
    public async Task Create_LateEntry_JudgesExpiryByPhysicalDay_AndFlagsLate()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-W5", null);
        // دیروز منقضی شده؛ اما جعبه‌ی ۳ روز قبل برداشته شده، پس آن روز هنوز معتبر بود.
        await App.AddBatchRawAsync(product, "LOT-EDGE", null, onHand: 5, expiry: Today.AddDays(-1));
        var line = (await AllocationAsync(Staff, product, 1)).Available.Single();
        var request = Req(product, (line, 1));
        request.OccurredAtUtc = DateTime.UtcNow.AddDays(-3);

        var response = await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.True(item.IsLate);
        Assert.False(item.ContainsExpired);
        Assert.True(item.AtUtc - item.OccurredAtUtc > TimeSpan.FromDays(2.9));
    }

    [Theory]
    [InlineData(-8 * 24 * 60)]
    [InlineData(30)]
    public async Task Create_TimeOutsideWindow_Returns400(int minutesFromNow)
    {
        var (product, _, _) = await TwoBatchesAsync();
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];
        var request = Req(product, (line, 1));
        request.OccurredAtUtc = DateTime.UtcNow.AddMinutes(minutesFromNow);

        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, request)).StatusCode);
    }

    [Fact]
    public async Task Create_Validation_EmptyLines_ZeroQuantity_DuplicateBatch_ForeignBatch()
    {
        var (product, _, _) = await TwoBatchesAsync();
        var other = await App.AddProductRawAsync("TEST-DRUG-W6", null);
        var foreign = await App.AddBatchRawAsync(other, "LOT-X", null, onHand: 5);
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];

        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, new WithdrawalRequest { ProductId = product })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1), (line, 1)))).StatusCode);

        var foreignRequest = new WithdrawalRequest { ProductId = product, Lines = [new() { BatchId = foreign, Quantity = 1, BatchVersion = 1 }] };
        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, foreignRequest)).StatusCode);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Create_InactiveProduct_Returns400()
    {
        var (product, _, _) = await TwoBatchesAsync();
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FirstAsync(p => p.Id == product)).IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).StatusCode);
    }

    [Fact]
    public async Task Endpoints_NeedWithdrawalCreate_AndCsrfHeader()
    {
        var (product, _, _) = await TwoBatchesAsync();
        await App.AddUserWithoutRoleAsync("nobody");
        var nobody = await LoggedInAsync("nobody", TestApp.StaffPassword);
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(ApiRoutes.ProductAllocation(product, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.GetAsync(ApiRoutes.Withdrawals)).StatusCode);

        var noCsrf = App.NewClient(csrfHeader: false);
        await TestApp.LoginAsync(noCsrf, "staff-a", TestApp.StaffPassword);
        Assert.NotEqual(HttpStatusCode.OK, (await noCsrf.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnWithdrawals_NewestFirst()
    {
        var (product, _, _) = await TwoBatchesAsync();
        var other = await LoggedInAsync("staff-b", TestApp.StaffPassword);
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];

        var first = await (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).Content.ReadFromJsonAsync<WithdrawalItem>();
        line = (await AllocationAsync(Staff, product, 1)).Lines[0];
        var second = await (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).Content.ReadFromJsonAsync<WithdrawalItem>();
        line = (await AllocationAsync(Staff, product, 1)).Lines[0];
        await other.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)));

        var mine = await Staff.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.Withdrawals);

        Assert.Equal([second!.Id, first!.Id], mine!.Select(w => w.Id));
        Assert.All(mine!, w => Assert.Equal("staff-a", w.UserName));
    }

    [Fact]
    public async Task Create_WritesAuditRowForWithdrawal_WithoutTouchingProductHistory()
    {
        var (product, _, _) = await TwoBatchesAsync();
        var line = (await AllocationAsync(Staff, product, 1)).Lines[0];
        var item = (await (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, Req(product, (line, 1)))).Content.ReadFromJsonAsync<WithdrawalItem>())!;

        Assert.Equal(1, await App.CountAuditAsync(item.Id, "Withdrawal"));
    }

    [Fact]
    public async Task ProductHistory_HidesUnitCost_FromUsersWithoutReportsView()
    {
        var keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
        var product = await App.AddProductRawAsync("TEST-DRUG-W7", null);
        var receipt = new ReceiptRequest { ProductId = product, BatchNumber = "LOT-COST", Expiry = "15/03/2035", Quantity = 2, UnitCost = 987654 };
        Assert.Equal(HttpStatusCode.OK, (await keeper.PostAsJsonAsync(ApiRoutes.Receipts, receipt)).StatusCode);

        var staffHistory = await Staff.GetStringAsync(ApiRoutes.ProductHistory(product));
        var manager = await LoggedInAsync("manager-a", TestApp.StaffPassword);
        var managerHistory = await manager.GetStringAsync(ApiRoutes.ProductHistory(product));

        Assert.DoesNotContain("987654", staffHistory);
        Assert.DoesNotContain("UnitCost", staffHistory);
        Assert.Contains("987654", managerHistory);
    }

    [Fact]
    public async Task ProductList_NearestExpiryText_KeepsInputCalendarAndPrecision()
    {
        var keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
        var product = await App.AddProductRawAsync("TEST-DRUG-W8", null);
        var receipt = new ReceiptRequest { ProductId = product, BatchNumber = "LOT-J", Expiry = "04/1450", Quantity = 2 };
        Assert.Equal(HttpStatusCode.OK, (await keeper.PostAsJsonAsync(ApiRoutes.Receipts, receipt)).StatusCode);

        var list = await Staff.GetFromJsonAsync<List<ProductListItem>>(ApiRoutes.Products + "?q=TEST-DRUG-W8");

        var item = Assert.Single(list!);
        Assert.Equal(JalaliDate.ToPersianDigits("04/1450"), item.NearestExpiryText);
    }
}
