using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

// موجودی بعد از حرکت: جمع تجمعی به ترتیب ثبت (AtUtc، سپس Id)، از همه‌ی حرکت‌های قلم، نه فقط فهرست نمایش‌داده‌شده.
public class MovementBalanceTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;
    private HttpClient Manager { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-b");
        await App.AddManagerAsync("manager-b");
        Keeper = await LoggedInAsync("keeper-b", TestApp.StaffPassword);
        Manager = await LoggedInAsync("manager-b", TestApp.StaffPassword);
    }

    // حرکت را مستقیم با زمان دلخواه می‌نویسد (ledger زمان AtUtc را خودش می‌گذارد، پس برای ترتیب دقیق این‌جا نوشته می‌شود).
    private async Task RawMovementAsync(Guid id, Guid batchId, StockMovementKind kind, int quantity, DateTime atUtc, DateTime occurredAtUtc)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var batch = await db.Batches.SingleAsync(b => b.Id == batchId);
        batch.OnHand += quantity;
        db.StockMovements.Add(new StockMovement { Id = id, BatchId = batchId, Kind = kind, Quantity = quantity, AtUtc = atUtc, OccurredAtUtc = occurredAtUtc });
        await db.SaveChangesAsync();
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

    [Fact]
    public async Task ProductHistory_BalanceAfter_FollowsRecordingOrder_AcrossBatches()
    {
        var product = await App.AddProductRawAsync("TEST-BAL-1", null);
        var b1 = await App.AddBatchRawAsync(product, "LOT-BAL-1", null);
        var b2 = await App.AddBatchRawAsync(product, "LOT-BAL-2", null);
        var t0 = DateTime.UtcNow.AddDays(-1);

        // ترتیب درج عمداً با ترتیب ثبت فرق دارد؛ جمع باید بر اساس AtUtc باشد.
        // (درج هم باید طوری باشد که OnHand بچ در لحظه‌ی ذخیره منفی نشود: CHECK_Batches_OnHand.)
        var late = Guid.NewGuid(); var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var withdrawal = Guid.NewGuid(); var reversal = Guid.NewGuid();
        await RawMovementAsync(second, b2, StockMovementKind.Import, 5, t0.AddHours(1), t0.AddHours(1));
        await RawMovementAsync(reversal, b2, StockMovementKind.Reversal, 2, t0.AddHours(3), t0.AddHours(3));
        await RawMovementAsync(first, b1, StockMovementKind.Import, 10, t0, t0);
        await RawMovementAsync(withdrawal, b1, StockMovementKind.Withdrawal, -4, t0.AddHours(2), t0.AddHours(2));
        await RawMovementAsync(late, b1, StockMovementKind.Adjustment, -1, t0.AddHours(4), t0.AddHours(4));

        var history = (await Keeper.GetFromJsonAsync<ProductHistory>(ApiRoutes.ProductHistory(product)))!;
        var byId = history.Movements.ToDictionary(m => m.Id);

        // ترتیب ثبت: first(+10,B1) second(+5,B2) withdrawal(-4,B1) reversal(+2,B2) late(-1,B1)
        Assert.Equal(10, byId[first].BalanceAfter);  Assert.Equal(10, byId[first].BatchBalanceAfter);
        Assert.Equal(15, byId[second].BalanceAfter); Assert.Equal(5, byId[second].BatchBalanceAfter);
        Assert.Equal(11, byId[withdrawal].BalanceAfter); Assert.Equal(6, byId[withdrawal].BatchBalanceAfter);
        Assert.Equal(13, byId[reversal].BalanceAfter); Assert.Equal(7, byId[reversal].BatchBalanceAfter);
        Assert.Equal(12, byId[late].BalanceAfter); Assert.Equal(5, byId[late].BatchBalanceAfter);

        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task ProductHistory_SameTimestamp_IsOrderedById()
    {
        var product = await App.AddProductRawAsync("TEST-BAL-2", null);
        var batch = await App.AddBatchRawAsync(product, "LOT-BAL-3", null);
        var at = DateTime.UtcNow.AddDays(-1);

        // دو حرکت با زمان یکسان: ترتیب با Id (کوچک‌تر اول). پس موجودی اولی 3 و دومی 7 است.
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() }.OrderBy(g => g).ToArray();
        await RawMovementAsync(ids[1], batch, StockMovementKind.Import, 4, at, at);
        await RawMovementAsync(ids[0], batch, StockMovementKind.Import, 3, at, at);

        var byId = (await Keeper.GetFromJsonAsync<ProductHistory>(ApiRoutes.ProductHistory(product)))!.Movements.ToDictionary(m => m.Id);
        Assert.Equal(3, byId[ids[0]].BalanceAfter);
        Assert.Equal(7, byId[ids[1]].BalanceAfter);
    }

    [Fact]
    public async Task ProductHistory_BalanceAfterWithdrawalAndRejectedReversal_MatchesStock()
    {
        var product = await App.AddProductRawAsync("TEST-BAL-3", null);
        var batch = await App.AddBatchRawAsync(product, "LOT-BAL-4", null, onHand: 10);
        var withdrawal = await WithdrawAsync(product, batch, 4);
        var reject = await Keeper.PostAsJsonAsync(ApiRoutes.WithdrawalReject(withdrawal.Id), new ReviewRequest { Version = withdrawal.Version, Note = "TEST برگشت" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var movements = (await Keeper.GetFromJsonAsync<ProductHistory>(ApiRoutes.ProductHistory(product)))!.Movements;
        Assert.Equal(10, movements.Single(m => m.Kind == StockMovementKind.Import).BalanceAfter);
        Assert.Equal(6, movements.Single(m => m.Kind == StockMovementKind.Withdrawal).BalanceAfter);
        Assert.Equal(10, movements.Single(m => m.Kind == StockMovementKind.Reversal).BalanceAfter);
        Assert.Equal(10, movements.Single(m => m.Kind == StockMovementKind.Reversal).BatchBalanceAfter);

        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Report_ProductFilter_ShowsBalanceFromAllMovementsOutsideWindow()
    {
        var product = await App.AddProductRawAsync("TEST-BAL-4", null);
        var b1 = await App.AddBatchRawAsync(product, "LOT-BAL-5", null);
        var b2 = await App.AddBatchRawAsync(product, "LOT-BAL-6", null);
        var now = DateTime.UtcNow;

        // حرکت قدیمی‌تر از بازه‌ی ۷ روز؛ در نتیجه‌ی ردیف‌های داخل بازه باید حساب شود.
        await RawMovementAsync(Guid.NewGuid(), b1, StockMovementKind.Import, 10, now.AddDays(-10), now.AddDays(-10));
        await RawMovementAsync(Guid.NewGuid(), b2, StockMovementKind.Import, 5, now.AddDays(-2), now.AddDays(-2));
        await RawMovementAsync(Guid.NewGuid(), b1, StockMovementKind.Withdrawal, -3, now.AddDays(-1), now.AddDays(-1));
        await RawMovementAsync(Guid.NewGuid(), b2, StockMovementKind.Reversal, 1, now.AddHours(-12), now.AddHours(-12));

        var newest = await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?days=7&productId={product}");
        Assert.Equal(3, newest!.Total);
        Assert.Equal(new[] { StockMovementKind.Reversal, StockMovementKind.Withdrawal, StockMovementKind.Import }, newest.Items.Select(i => i.Kind).ToArray());
        Assert.Equal(new int?[] { 13, 12, 15 }, newest.Items.Select(i => i.ProductBalanceAfter).ToArray());

        // فیلتر نوع و صفحه‌بندی روی مقدار اثر نمی‌گذارد.
        var oldest = await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?days=7&productId={product}&oldestFirst=true&skip=1&take=1");
        Assert.Equal(StockMovementKind.Withdrawal, Assert.Single(oldest!.Items).Kind);
        Assert.Equal(12, Assert.Single(oldest.Items).ProductBalanceAfter);

        var withdrawalsOnly = await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?days=7&productId={product}&kind=Withdrawal");
        Assert.Equal(12, Assert.Single(withdrawalsOnly!.Items).ProductBalanceAfter);
    }

    [Fact]
    public async Task Report_WithoutProductFilter_HasNoBalanceAfter_AndSortsOldestFirst()
    {
        var product = await App.AddProductRawAsync("TEST-BAL-5", null);
        var batch = await App.AddBatchRawAsync(product, "LOT-BAL-7", null);
        var now = DateTime.UtcNow;
        await RawMovementAsync(Guid.NewGuid(), batch, StockMovementKind.Import, 8, now.AddDays(-3), now.AddDays(-3));
        await RawMovementAsync(Guid.NewGuid(), batch, StockMovementKind.Withdrawal, -2, now.AddDays(-1), now.AddDays(-1));

        var all = await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?days=7");
        Assert.All(all!.Items, i => Assert.Null(i.ProductBalanceAfter));

        var oldest = await Manager.GetFromJsonAsync<MovementReportResponse>($"{ApiRoutes.ReportMovements}?days=7&productId={product}&oldestFirst=true");
        Assert.Equal(new[] { StockMovementKind.Import, StockMovementKind.Withdrawal }, oldest!.Items.Select(i => i.Kind).ToArray());
        Assert.Equal(new int?[] { 8, 6 }, oldest.Items.Select(i => i.ProductBalanceAfter).ToArray());
    }
}
