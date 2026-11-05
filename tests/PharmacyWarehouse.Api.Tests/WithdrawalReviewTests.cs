using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Tests;

public class WithdrawalReviewTests : ApiTestBase
{
    private HttpClient Staff { get; set; } = null!;
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddStaffAsync("staff-a");
        await App.AddKeeperAsync("keeper-a");
        Staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private static DateOnly Today => LocalDay.Today(DateTime.UtcNow);

    // دو بچ (۴ و ۱۰) و یک برداشت ۶ تایی در دو بچ، ثبت‌شده توسط کارمند.
    private async Task<(Guid Product, Guid Early, Guid Late, WithdrawalItem Item)> WithdrawalAsync(HttpClient? by = null, int quantity = 6, string name = "TEST-DRUG-R1")
    {
        var client = by ?? Staff;
        var product = await App.AddProductRawAsync(name, null);
        var late = await App.AddBatchRawAsync(product, "LOT-LATE", null, onHand: 10, expiry: Today.AddDays(400));
        var early = await App.AddBatchRawAsync(product, "LOT-EARLY", null, onHand: 4, expiry: Today.AddDays(60));
        var allocation = (await client.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, quantity)))!;
        var request = new WithdrawalRequest
        {
            ProductId = product,
            Lines = allocation.Lines.Select(l => new WithdrawalLineRequest { BatchId = l.BatchId, Quantity = l.Quantity, BatchVersion = l.Version }).ToList()
        };
        var response = await client.PostAsJsonAsync(ApiRoutes.Withdrawals, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (product, early, late, (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!);
    }

    private Task<HttpResponseMessage> ApproveAsync(HttpClient client, WithdrawalItem item, int? version = null) =>
        client.PostAsJsonAsync(ApiRoutes.WithdrawalApprove(item.Id), new ReviewRequest { Version = version ?? item.Version });

    private Task<HttpResponseMessage> RejectAsync(HttpClient client, WithdrawalItem item, string? note, int? version = null) =>
        client.PostAsJsonAsync(ApiRoutes.WithdrawalReject(item.Id), new ReviewRequest { Version = version ?? item.Version, Note = note });

    private async Task<int> OnHandAsync(Guid batch)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        return (await db.Batches.AsNoTracking().FirstAsync(b => b.Id == batch)).OnHand;
    }

    [Fact]
    public async Task Approve_ChangesOnlyStatus_AndRecordsReviewer()
    {
        var (_, early, _, item) = await WithdrawalAsync();
        var before = await OnHandAsync(early);

        var response = await ApproveAsync(Keeper, item);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.Equal(WithdrawalStatus.Approved, approved.Status);
        Assert.Equal("keeper-a", approved.ReviewedByName);
        Assert.NotNull(approved.ReviewedAtUtc);
        Assert.False(approved.SelfReviewed);
        Assert.Equal(item.Version + 1, approved.Version);
        Assert.Equal(before, await OnHandAsync(early));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Reject_RequiresNote()
    {
        var (_, _, _, item) = await WithdrawalAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await RejectAsync(Keeper, item, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RejectAsync(Keeper, item, "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RejectAsync(Keeper, item, new string('ا', Withdrawal.MaxReviewNote + 1))).StatusCode);
    }

    [Fact]
    public async Task Reject_PostsReversalPerMovement_RestoresOnHand_AndKeepsLedgerConsistent()
    {
        var (_, early, late, item) = await WithdrawalAsync();
        Assert.Equal(0, await OnHandAsync(early));
        Assert.Equal(8, await OnHandAsync(late));

        var response = await RejectAsync(Keeper, item, "تعداد اشتباه ثبت شده");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = (await response.Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.Equal(WithdrawalStatus.Rejected, rejected.Status);
        Assert.Equal("تعداد اشتباه ثبت شده", rejected.ReviewNote);
        Assert.Equal(4, await OnHandAsync(early));
        Assert.Equal(10, await OnHandAsync(late));

        var movements = await App.MovementsAsync(early);
        var withdrawal = Assert.Single(movements, m => m.Kind == StockMovementKind.Withdrawal);
        var reversal = Assert.Single(movements, m => m.Kind == StockMovementKind.Reversal);
        Assert.Equal(4, reversal.Quantity);
        Assert.Equal(withdrawal.Id, reversal.ReversesId);
        Assert.Equal(item.Id, reversal.WithdrawalId);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Reject_WritesNoteAsAuditReason()
    {
        var (_, _, _, item) = await WithdrawalAsync();
        await RejectAsync(Keeper, item, "دلیل آزمایشی");

        var rows = await App.AuditRowsAsync(item.Id);

        Assert.Contains(rows, r => r.Field == "Status" && r.NewValue == "Rejected" && r.Reason == "دلیل آزمایشی");
    }

    [Fact]
    public async Task AlreadyReviewed_Returns409_ForApproveAndReject_AndMovesNoStock()
    {
        var (_, early, _, item) = await WithdrawalAsync();
        var rejected = (await (await RejectAsync(Keeper, item, "اشتباه")).Content.ReadFromJsonAsync<WithdrawalItem>())!;
        var after = await OnHandAsync(early);

        Assert.Equal(HttpStatusCode.Conflict, (await ApproveAsync(Keeper, rejected)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RejectAsync(Keeper, rejected, "دوباره")).StatusCode);
        Assert.Equal(after, await OnHandAsync(early)); // دو بار برنگشته
        await App.AssertLedgerConsistentAsync();

        var (_, _, _, second) = await WithdrawalAsync(name: "TEST-DRUG-R2");
        var approved = (await (await ApproveAsync(Keeper, second)).Content.ReadFromJsonAsync<WithdrawalItem>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await RejectAsync(Keeper, approved, "دیر شد")).StatusCode);
    }

    [Fact]
    public async Task StaleVersion_Returns409VersionConflict()
    {
        var (_, _, _, item) = await WithdrawalAsync();

        var response = await ApproveAsync(Keeper, item, version: item.Version + 5);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(ProblemTypes.VersionConflict, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reject_StaleVersion_ChangesNoStock()
    {
        var (_, early, _, item) = await WithdrawalAsync();

        var response = await RejectAsync(Keeper, item, "اشتباه", version: item.Version + 5);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await OnHandAsync(early));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task KeeperMaySelfReview_AndItIsFlagged()
    {
        var (_, _, _, item) = await WithdrawalAsync(by: Keeper);

        var approved = (await (await ApproveAsync(Keeper, item)).Content.ReadFromJsonAsync<WithdrawalItem>())!;

        Assert.True(approved.SelfReviewed);
        Assert.Equal("keeper-a", approved.UserName);
    }

    [Fact]
    public async Task StaffCannotReview_AnyEndpoint()
    {
        var (_, _, _, item) = await WithdrawalAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.GetAsync(ApiRoutes.WithdrawalReviewList)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ApproveAsync(Staff, item)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await RejectAsync(Staff, item, "x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Staff.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove, new BulkApproveRequest { Items = [new() { Id = item.Id, Version = item.Version }] })).StatusCode);
    }

    [Fact]
    public async Task ReviewList_DefaultsToPending_OldestFirst_AndFiltersByStatus()
    {
        var (_, _, _, first) = await WithdrawalAsync(name: "TEST-DRUG-R3");
        var (_, _, _, second) = await WithdrawalAsync(name: "TEST-DRUG-R4");
        var (_, _, _, third) = await WithdrawalAsync(name: "TEST-DRUG-R5");
        await ApproveAsync(Keeper, second);
        await RejectAsync(Keeper, third, "اشتباه");

        var pending = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList))!;
        var approved = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList + "?status=Approved"))!;
        var all = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList + "?status=all"))!;

        Assert.Equal(first.Id, Assert.Single(pending).Id);
        Assert.Equal(second.Id, Assert.Single(approved).Id);
        Assert.Equal([third.Id, second.Id, first.Id], all.Select(w => w.Id)); // تازه‌ترین اول
        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.GetAsync(ApiRoutes.WithdrawalReviewList + "?status=bogus")).StatusCode);
    }

    [Fact]
    public async Task ReviewList_ShowsLateAndExpiredFlags()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-R6", null);
        await App.AddBatchRawAsync(product, "LOT-OLD", null, onHand: 5, expiry: Today.AddDays(-2));
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FirstAsync(p => p.Id == product)).AllowExpiredWithdrawal = true;
            await db.SaveChangesAsync();
        }

        var line = (await Staff.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, 1)))!.Available.Single();
        var request = new WithdrawalRequest { ProductId = product, Lines = [new() { BatchId = line.BatchId, Quantity = 1, BatchVersion = line.Version }] };
        Assert.Equal(HttpStatusCode.OK, (await Staff.PostAsJsonAsync(ApiRoutes.Withdrawals, request)).StatusCode);

        var item = Assert.Single((await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList))!);

        Assert.True(item.ContainsExpired);
        Assert.False(item.IsLate);
    }

    [Fact]
    public async Task BulkApprove_IsPartial_StaleAndAlreadyReviewedFailWithoutBlockingOthers()
    {
        var (_, _, _, a) = await WithdrawalAsync(name: "TEST-DRUG-R7");
        var (_, _, _, b) = await WithdrawalAsync(name: "TEST-DRUG-R8");
        var (_, _, _, c) = await WithdrawalAsync(name: "TEST-DRUG-R9");
        await RejectAsync(Keeper, c, "اشتباه");

        var request = new BulkApproveRequest
        {
            Items =
            [
                new() { Id = a.Id, Version = a.Version },
                new() { Id = b.Id, Version = b.Version + 9 }, // کهنه
                new() { Id = c.Id, Version = c.Version }, // قبلاً بررسی شده
                new() { Id = Guid.NewGuid(), Version = 1 } // ناموجود
            ]
        };

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BulkApproveResult>())!;
        Assert.Equal([a.Id], result.Approved);
        Assert.Equal(3, result.Failed.Count);
        Assert.Contains(result.Failed, f => f.Id == b.Id);

        var approved = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList + "?status=Approved"))!;
        Assert.Equal(a.Id, Assert.Single(approved).Id);
        var pending = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList))!;
        Assert.Equal(b.Id, Assert.Single(pending).Id); // کهنه‌ها هنوز در انتظارند
    }

    [Fact]
    public async Task BulkApprove_ValidItemsAfterAFailure_AreStillApproved_AndFailedItemLeavesNoAudit()
    {
        var (_, _, _, stale) = await WithdrawalAsync(name: "TEST-DRUG-R10");
        var (_, _, _, good1) = await WithdrawalAsync(name: "TEST-DRUG-R11");
        var (_, _, _, good2) = await WithdrawalAsync(name: "TEST-DRUG-R12");

        var request = new BulkApproveRequest
        {
            Items = [new() { Id = stale.Id, Version = stale.Version + 9 }, new() { Id = good1.Id, Version = good1.Version }, new() { Id = good2.Id, Version = good2.Version }]
        };
        var result = (await (await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove, request)).Content.ReadFromJsonAsync<BulkApproveResult>())!;

        Assert.Equal([good1.Id, good2.Id], result.Approved);
        var failure = Assert.Single(result.Failed);
        Assert.Equal(stale.Id, failure.Id);
        Assert.False(string.IsNullOrWhiteSpace(failure.Message));
        // اگر ChangeTracker پاک نمی‌شد، ردیف تاریخچه‌ی برداشت کهنه هم ذخیره می‌شد.
        Assert.DoesNotContain(await App.AuditRowsAsync(stale.Id), r => r.Field == "Status");
        Assert.Contains(await App.AuditRowsAsync(good2.Id), r => r.Field == "Status" && r.NewValue == "Approved");
    }

    [Fact]
    public async Task BulkApprove_DuplicateIds_Returns400()
    {
        var (_, _, _, item) = await WithdrawalAsync(name: "TEST-DRUG-R13");
        var request = new BulkApproveRequest { Items = [new() { Id = item.Id, Version = item.Version + 9 }, new() { Id = item.Id, Version = item.Version }] };

        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove, request)).StatusCode);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("Pending,Approved")]
    public async Task ReviewList_NumericOrListStatus_Returns400(string status) =>
        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.GetAsync(ApiRoutes.WithdrawalReviewList + "?status=" + status)).StatusCode);

    [Fact]
    public async Task ReviewList_PendingOrder_IsOldestFirst()
    {
        var (_, _, _, first) = await WithdrawalAsync(name: "TEST-DRUG-R14");
        var (_, _, _, second) = await WithdrawalAsync(name: "TEST-DRUG-R15");

        var pending = (await Keeper.GetFromJsonAsync<List<WithdrawalItem>>(ApiRoutes.WithdrawalReviewList))!;

        Assert.Equal([first.Id, second.Id], pending.Select(w => w.Id));
    }

    [Fact]
    public async Task BulkApprove_EmptyOrNull_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalBulkApprove, new BulkApproveRequest())).StatusCode);
        var body = new StringContent("{\"items\":null}", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await Keeper.PostAsync(ApiRoutes.WithdrawalBulkApprove, body)).StatusCode);
    }

    [Fact]
    public async Task UnknownWithdrawal_Returns404()
    {
        var ghost = new WithdrawalItem(Guid.NewGuid(), Guid.Empty, "", null, null, 0, default, default, false, false, false,
            WithdrawalStatus.Pending, "", null, null, null, null, false, [], 1);

        Assert.Equal(HttpStatusCode.NotFound, (await ApproveAsync(Keeper, ghost)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await RejectAsync(Keeper, ghost, "x")).StatusCode);
    }
}
