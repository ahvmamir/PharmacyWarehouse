using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class QuickReceiptTests : ApiTestBase
{
    private HttpClient Staff { get; set; } = null!;
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddStaffAsync("staff-q");
        await App.AddKeeperAsync("keeper-q");
        Staff = await LoggedInAsync("staff-q", TestApp.StaffPassword);
        Keeper = await LoggedInAsync("keeper-q", TestApp.StaffPassword);
    }

    private static QuickReceiptLineRequest Named(string name, string batch = "TEST-BATCH-01", int quantity = 5) =>
        new() { NewName = name, BatchNumber = batch, Quantity = quantity };

    private static QuickReceiptLineRequest Existing(Guid product, string batch = "TEST-BATCH-01", int quantity = 5) =>
        new() { ProductId = product, BatchNumber = batch, Quantity = quantity };

    private async Task<ReceiptItem> SubmitAsync(params QuickReceiptLineRequest[] lines)
    {
        var response = await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest { Lines = [.. lines] });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptItem>())!;
    }

    private static ReviewLineEdit Keep(ReceiptLineItem l, int? quantity = null) => new()
    {
        LineId = l.Id, LineVersion = l.Version, BatchVersion = l.BatchVersion, Quantity = quantity ?? l.Quantity,
        BatchLocationId = l.BatchLocationId
    };

    private Task<HttpResponseMessage> ReviewAsync(ReceiptItem receipt, params ReviewLineEdit[] edits) =>
        Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReview(receipt.Id), new ReviewReceiptRequest { Version = receipt.Version, Lines = [.. edits] });

    private async Task<ReceiptItem> GetAsync(Guid id) => (await Keeper.GetFromJsonAsync<ReceiptItem>(ApiRoutes.ReceiptById(id)))!;

    private async Task<T> DbAsync<T>(Func<WarehouseDbContext, Task<T>> read)
    {
        using var scope = App.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<WarehouseDbContext>());
    }

    private async Task<Guid> AddLocationAsync(string name)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var location = new StorageLocation { Id = Guid.NewGuid(), Name = name, NameKey = name.ToLowerInvariant(), ParentKey = "" };
        db.StorageLocations.Add(location);
        await db.SaveChangesAsync();
        return location.Id;
    }

    private Task WithdrawAsync(Guid product, int quantity) => WithdrawAsync(Staff, product, quantity);

    private async Task WithdrawAsync(HttpClient client, Guid product, int quantity)
    {
        var allocation = (await client.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, quantity)))!;
        Assert.Equal(0, allocation.Shortfall);
        var request = new WithdrawalRequest
        {
            ProductId = product,
            Lines = allocation.Lines.Select(l => new WithdrawalLineRequest { BatchId = l.BatchId, Quantity = l.Quantity, BatchVersion = l.Version }).ToList()
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(ApiRoutes.Withdrawals, request)).StatusCode);
    }

    [Fact]
    public async Task Submit_NewName_CreatesProvisionalProduct_AndAddsStockImmediately()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q1", quantity: 12));

        Assert.Equal(ReceiptStatus.Pending, receipt.Status);
        var line = Assert.Single(receipt.Lines);
        Assert.True(line.IsProvisional);
        Assert.False(line.RequiresExpiry);
        Assert.Equal(12, line.OnHand);
        Assert.Null(line.ExpiryText);
        var movement = Assert.Single(await App.MovementsAsync(line.BatchId));
        Assert.Equal(StockMovementKind.Receipt, movement.Kind);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Submit_ExistingProductAndBatch_AddsToSameBatch()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-Q2", null);
        var batch = await App.AddBatchRawAsync(product, "TEST-BATCH-01", null, onHand: 3, expiry: new DateOnly(2031, 1, 1));

        var receipt = await SubmitAsync(Existing(product, "test-batch-01", 4));

        Assert.Equal(batch, receipt.Lines[0].BatchId);
        Assert.Equal(7, receipt.Lines[0].OnHand);
        Assert.Equal(2, (await App.MovementsAsync(batch)).Count);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Submit_SameNewNameTwice_SharesOneProvisionalProduct_AndSameBatchMerges()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q3", "A1", 2), Named("test-drug-q3", "A1", 3), Named("TEST-DRUG-Q3", "A2", 1));

        Assert.Single(receipt.Lines.Select(l => l.ProductId).Distinct());
        Assert.Equal(2, receipt.Lines.Select(l => l.BatchId).Distinct().Count());
        Assert.Equal(1, await DbAsync(db => db.Products.CountAsync(p => p.IsProvisional)));
        Assert.Equal(5, receipt.Lines[0].OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Submit_NewNameMatchingExistingProduct_UsesExisting()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-Q4", null);

        var receipt = await SubmitAsync(Named("TEST-DRUG-Q4"));

        Assert.Equal(product, receipt.Lines[0].ProductId);
        Assert.False(receipt.Lines[0].IsProvisional);
    }

    [Theory]
    [InlineData(0, "Lines[0].Quantity")]
    [InlineData(-3, "Lines[0].Quantity")]
    public async Task Submit_InvalidQuantity_Is400_AndSavesNothing(int quantity, string key)
    {
        var response = await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest { Lines = [Named("TEST-DRUG-Q5", quantity: quantity)] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(key, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await DbAsync(db => db.Receipts.CountAsync()));
        Assert.Equal(0, await DbAsync(db => db.Products.CountAsync()));
    }

    [Fact]
    public async Task Submit_NeitherOrBothProductAndName_Is400()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-Q6", null);
        var both = new QuickReceiptLineRequest { ProductId = product, NewName = "X", BatchNumber = "B", Quantity = 1 };
        var neither = new QuickReceiptLineRequest { BatchNumber = "B", Quantity = 1 };

        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest { Lines = [both] })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest { Lines = [neither] })).StatusCode);
    }

    [Fact]
    public async Task Submit_OneBadLine_SavesNothing()
    {
        var response = await Staff.PostAsJsonAsync(ApiRoutes.ReceiptsQuick,
            new QuickReceiptRequest { Lines = [Named("TEST-DRUG-Q7"), Named("TEST-DRUG-Q8", quantity: 0)] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await DbAsync(db => db.StockMovements.CountAsync()));
    }

    [Fact]
    public async Task Staff_CanWithdrawProvisionalStock_BeforeReview_AndNoExpiryBatchSortsLast()
    {
        var product = await App.AddProductRawAsync("TEST-DRUG-Q9", null);
        await App.AddBatchRawAsync(product, "DATED", null, onHand: 2, expiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(300));
        await SubmitAsync(Existing(product, "UNDATED", 5));

        var allocation = (await Staff.GetFromJsonAsync<AllocationResponse>(ApiRoutes.ProductAllocation(product, 4)))!;

        Assert.Equal(["DATED", "UNDATED"], allocation.Lines.Select(l => l.BatchNumber));
        await WithdrawAsync(product, 4);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Mine_ListsOnlyOwnReceipts_AndPendingNeedsReceivePermission()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q10"));
        await App.AddStaffAsync("staff-other");
        var other = await LoggedInAsync("staff-other", TestApp.StaffPassword);

        var mine = (await Staff.GetFromJsonAsync<List<ReceiptItem>>(ApiRoutes.ReceiptsMine))!;
        var theirs = (await other.GetFromJsonAsync<List<ReceiptItem>>(ApiRoutes.ReceiptsMine))!;

        Assert.Equal(receipt.Id, Assert.Single(mine).Id);
        Assert.Empty(theirs);
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.GetAsync(ApiRoutes.ReceiptsPending)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(ApiRoutes.ReceiptById(receipt.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Staff.GetAsync(ApiRoutes.ReceiptById(receipt.Id))).StatusCode);
        Assert.Single((await Keeper.GetFromJsonAsync<List<ReceiptItem>>(ApiRoutes.ReceiptsPending))!);
    }

    [Fact]
    public async Task Permissions_UserWithoutReceiveOrSubmit_IsForbidden()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q11"));
        await App.AddUserWithoutRoleAsync("nobody");
        var nobody = await LoggedInAsync("nobody", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await nobody.PostAsJsonAsync(ApiRoutes.ReceiptsQuick, new QuickReceiptRequest { Lines = [Named("X")] })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.PostAsJsonAsync(ApiRoutes.ReceiptApprove(receipt.Id), new ReviewRequest { Version = receipt.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.PostAsJsonAsync(ApiRoutes.ReceiptReject(receipt.Id), new ReviewRequest { Version = receipt.Version, Note = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Staff.PostAsJsonAsync(ApiRoutes.ReceiptReview(receipt.Id), new ReviewReceiptRequest { Version = receipt.Version })).StatusCode);
    }

    [Fact]
    public async Task Review_QuantityChange_PostsAdjustmentDifference_NeverEditsMovement()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q12", quantity: 10));
        var line = receipt.Lines[0];

        var response = await ReviewAsync(receipt, Keep(line, quantity: 7));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<ReceiptItem>())!;
        Assert.Equal(7, saved.Lines[0].Quantity);
        Assert.Equal(7, saved.Lines[0].OnHand);
        var movements = await App.MovementsAsync(line.BatchId);
        Assert.Equal([StockMovementKind.Receipt, StockMovementKind.Adjustment], movements.Select(m => m.Kind));
        Assert.Equal([10, -3], movements.Select(m => m.Quantity));
        Assert.Equal("اصلاح ورود", movements[1].Note);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Review_QuantityBelowWithdrawn_Is409()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q13", quantity: 10));
        await WithdrawAsync(receipt.Lines[0].ProductId, 8);
        receipt = await GetAsync(receipt.Id);

        var response = await ReviewAsync(receipt, Keep(receipt.Lines[0], quantity: 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, (await GetAsync(receipt.Id)).Lines[0].OnHand);
    }

    [Fact]
    public async Task Review_EditsProductBatchExpiryAndLocation_ClearsProvisional()
    {
        var location = await AddLocationAsync("TEST-SHELF");
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q14"));
        var line = receipt.Lines[0];
        var edit = Keep(line);
        edit.Product = new ReviewProductEdit { Name = "TEST-DRUG-Q14", Brand = "TEST-BRAND", PackDescription = "30", LocationId = location, RequiresExpiry = true, Version = line.ProductVersion };
        edit.Expiry = "15/03/2030";
        edit.BatchLocationId = location;

        var response = await ReviewAsync(receipt, edit);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<ReceiptItem>())!.Lines[0];
        Assert.False(saved.IsProvisional);
        Assert.True(saved.RequiresExpiry);
        Assert.Equal("TEST-BRAND", saved.Brand);
        Assert.NotNull(saved.ExpiryText);
        Assert.Equal(location, saved.BatchLocationId);
    }

    [Fact]
    public async Task Review_StaleVersions_Are409()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q15"));
        var line = receipt.Lines[0];

        var staleReceipt = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReview(receipt.Id),
            new ReviewReceiptRequest { Version = receipt.Version + 5, Lines = [Keep(line)] });
        var staleBatch = Keep(line);
        staleBatch.BatchVersion += 3;
        var staleBatchResponse = await ReviewAsync(receipt, staleBatch);

        Assert.Equal(HttpStatusCode.Conflict, staleReceipt.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, staleBatchResponse.StatusCode);
    }

    [Fact]
    public async Task Review_MergeProvisionalIntoExisting_MovesBatchAndWithdrawals_AndDeactivatesProvisional()
    {
        var target = await App.AddProductRawAsync("TEST-DRUG-Q16", null);
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q16-typo", quantity: 6));
        var line = receipt.Lines[0];
        await WithdrawAsync(line.ProductId, 2);
        receipt = await GetAsync(receipt.Id);
        var edit = Keep(receipt.Lines[0]);
        edit.MergeIntoProductId = target;

        var response = await ReviewAsync(receipt, edit);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<ReceiptItem>())!.Lines[0];
        Assert.Equal(target, saved.ProductId);
        Assert.Equal(4, saved.OnHand);
        Assert.False(await DbAsync(db => db.Products.Where(p => p.Id == line.ProductId).Select(p => p.IsActive).SingleAsync()));
        Assert.Equal(target, await DbAsync(db => db.Withdrawals.Select(w => w.ProductId).SingleAsync()));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Review_MergeWithClashingBatchNumber_Is409()
    {
        var target = await App.AddProductRawAsync("TEST-DRUG-Q17", null);
        await App.AddBatchRawAsync(target, "TEST-BATCH-01", null, onHand: 1);
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q17-typo"));
        var edit = Keep(receipt.Lines[0]);
        edit.MergeIntoProductId = target;

        Assert.Equal(HttpStatusCode.Conflict, (await ReviewAsync(receipt, edit)).StatusCode);
    }

    [Fact]
    public async Task Approve_SetsStatus_ClearsProvisional_AndKeepsStock()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q18", quantity: 4));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptApprove(receipt.Id), new ReviewRequest { Version = receipt.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var approved = (await response.Content.ReadFromJsonAsync<ReceiptItem>())!;
        Assert.Equal(ReceiptStatus.Approved, approved.Status);
        Assert.Equal("keeper-q", approved.ReviewedByName);
        Assert.False(approved.Lines[0].IsProvisional);
        Assert.Equal(4, approved.Lines[0].OnHand);
        Assert.Empty((await Keeper.GetFromJsonAsync<List<ReceiptItem>>(ApiRoutes.ReceiptsPending))!);
        // دوباره بررسی ← ۴۰۹
        Assert.Equal(HttpStatusCode.Conflict, (await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptApprove(receipt.Id), new ReviewRequest { Version = approved.Version })).StatusCode);
    }

    [Fact]
    public async Task Approve_RequiredExpiryMissing_Is400()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q19"));
        var edit = Keep(receipt.Lines[0]);
        edit.Product = new ReviewProductEdit { Name = "TEST-DRUG-Q19", RequiresExpiry = true, Version = receipt.Lines[0].ProductVersion };
        var saved = (await (await ReviewAsync(receipt, edit)).Content.ReadFromJsonAsync<ReceiptItem>())!;

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptApprove(saved.Id), new ReviewRequest { Version = saved.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Lines[0].Expiry", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reject_PostsReversal_ReturnsStockToZero_AndNeedsNote()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q20", quantity: 9));
        var batch = receipt.Lines[0].BatchId;

        var noNote = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReject(receipt.Id), new ReviewRequest { Version = receipt.Version });
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReject(receipt.Id), new ReviewRequest { Version = receipt.Version, Note = "اشتباه ثبت شد" });

        Assert.Equal(HttpStatusCode.BadRequest, noNote.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rejected = (await response.Content.ReadFromJsonAsync<ReceiptItem>())!;
        Assert.Equal(ReceiptStatus.Rejected, rejected.Status);
        Assert.Equal(0, rejected.Lines[0].OnHand);
        var movements = await App.MovementsAsync(batch);
        Assert.Equal([StockMovementKind.Receipt, StockMovementKind.Reversal], movements.Select(m => m.Kind));
        Assert.Equal(movements[0].Id, movements[1].ReversesId);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Reject_AfterQuantityAdjusted_ReversesCurrentQuantity()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q21", quantity: 10));
        var saved = (await (await ReviewAsync(receipt, Keep(receipt.Lines[0], quantity: 6))).Content.ReadFromJsonAsync<ReceiptItem>())!;

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReject(saved.Id), new ReviewRequest { Version = saved.Version, Note = "رد" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<ReceiptItem>())!.Lines[0].OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Reject_AfterStockWithdrawn_Is409_AndNothingChanges()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q22", quantity: 5));
        await WithdrawAsync(receipt.Lines[0].ProductId, 3);
        receipt = await GetAsync(receipt.Id);

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptReject(receipt.Id), new ReviewRequest { Version = receipt.Version, Note = "رد" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var after = await GetAsync(receipt.Id);
        Assert.Equal(ReceiptStatus.Pending, after.Status);
        Assert.Equal(2, after.Lines[0].OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Alert_PendingReceipts_ShownToKeeperOnly()
    {
        await SubmitAsync(Named("TEST-DRUG-Q23"));

        var keeperAlerts = (await Keeper.GetFromJsonAsync<List<AlertItem>>(ApiRoutes.Alerts))!;
        var staffAlerts = (await Staff.GetFromJsonAsync<List<AlertItem>>(ApiRoutes.Alerts))!;

        Assert.Equal(1, keeperAlerts.Single(a => a.Key == AlertKeys.ReceiptsPending).Count);
        Assert.DoesNotContain(staffAlerts, a => a.Key == AlertKeys.ReceiptsPending);
    }

    [Fact]
    public async Task Audit_RecordsReceiptAndLineChanges()
    {
        var receipt = await SubmitAsync(Named("TEST-DRUG-Q24"));

        Assert.True(await App.CountAuditAsync(receipt.Id, nameof(Receipt)) > 0);
        Assert.True(await App.CountAuditAsync(receipt.Lines[0].Id, nameof(ReceiptLine)) > 0);
    }
}
