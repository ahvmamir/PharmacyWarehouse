using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Tests;

public class ReceiptTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        await App.AddStaffAsync("staff-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private static ReceiptRequest Req(Guid productId, string batch = "TEST-BATCH-01", string? expiry = "15/03/2030", int quantity = 5, int? version = null) =>
        new() { ProductId = productId, BatchNumber = batch, Expiry = expiry, Quantity = quantity, BatchVersion = version };

    private async Task<ReceiptResult> ReceiveOkAsync(ReceiptRequest request)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReceiptResult>())!;
    }

    private async Task<Guid> ProductAsync(string name = "TEST-DRUG-01", bool requiresExpiry = true)
    {
        var id = await App.AddProductRawAsync(name, null);
        if (!requiresExpiry)
        {
            using var scope = App.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            var product = await db.Products.FirstAsync(p => p.Id == id);
            product.RequiresExpiry = false;
            await db.SaveChangesAsync();
        }

        return id;
    }

    [Fact]
    public async Task NewBatch_CreatesBatchAndMovement_AndLedgerIsConsistent()
    {
        var product = await ProductAsync();

        var result = await ReceiveOkAsync(Req(product, quantity: 7));

        Assert.True(result.CreatedBatch);
        Assert.Equal(7, result.Batch.OnHand);
        Assert.Equal("TEST-BATCH-01", result.Batch.BatchNumber);
        Assert.Equal(new DateOnly(2030, 3, 15), result.Batch.ExpiryDate);
        var movement = Assert.Single(await App.MovementsAsync(result.Batch.Id));
        Assert.Equal(StockMovementKind.Receipt, movement.Kind);
        Assert.Equal(7, movement.Quantity);
        // AtUtc همیشه اکنونِ سرور در StockLedger.Post است؛ برای ورود جنس با زمان واقعی فقط میلی‌ثانیه فرق دارد.
        Assert.True((movement.AtUtc - movement.OccurredAtUtc).Duration() < TimeSpan.FromSeconds(5));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task ExistingBatch_AddsQuantity()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product, quantity: 5));

        var second = await ReceiveOkAsync(Req(product, expiry: null, quantity: 3, version: first.Batch.Version));

        Assert.False(second.CreatedBatch);
        Assert.Equal(first.Batch.Id, second.Batch.Id);
        Assert.Equal(8, second.Batch.OnHand);
        Assert.Equal(2, (await App.MovementsAsync(first.Batch.Id)).Count);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task ExistingBatch_DifferentExpiry_Returns409()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: "16/03/2030", version: first.Batch.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("یک بچ دو تاریخ ندارد", await response.Content.ReadAsStringAsync());
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task ExistingBatch_StaleVersion_Returns409()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product, quantity: 5));
        await ReceiveOkAsync(Req(product, expiry: null, quantity: 1, version: first.Batch.Version));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null, quantity: 1, version: first.Batch.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task ExistingBatch_MissingVersion_Returns409()
    {
        var product = await ProductAsync();
        await ReceiveOkAsync(Req(product));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null, version: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ExistingBatchNumber_WithoutVersion_Returns409WithBatchExistsMessage()
    {
        var product = await ProductAsync();
        await ReceiveOkAsync(Req(product));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null, version: null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("این بچ قبلاً ثبت شده است؛ آن را از فهرست بچ‌ها انتخاب کنید.", body.RootElement.GetProperty("title").GetString());
        var type = body.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
        Assert.NotEqual(ProblemTypes.VersionConflict, type);
    }

    [Fact]
    public async Task Receipt_WritesNoOnHandAuditRow()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product, quantity: 5));
        await ReceiveOkAsync(Req(product, expiry: null, quantity: 4, version: first.Batch.Version));

        var rows = await App.AuditRowsAsync(first.Batch.Id);

        Assert.DoesNotContain(rows, r => r.Field == "OnHand");
        Assert.DoesNotContain(rows.Where(r => r.Action == "Created"), r => r.NewValue!.Contains("OnHand"));
    }

    [Fact]
    public async Task CreatedBatchRow_StoresExpiryAsIsoDate()
    {
        var product = await ProductAsync();
        var result = await ReceiveOkAsync(Req(product));

        var row = Assert.Single(await App.AuditRowsAsync(result.Batch.Id));

        Assert.Contains("\"ExpiryDate\":\"2030-03-15\"", row.NewValue);
    }

    [Fact]
    public async Task ExistingBatch_ExpiryGivenButBatchHasNone_Returns409()
    {
        var product = await ProductAsync(requiresExpiry: false);
        var first = await ReceiveOkAsync(Req(product, expiry: null));

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: "15/03/2030", version: first.Batch.Version));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task JalaliMonthExpiry_StoredAsEndOfJalaliMonth()
    {
        var product = await ProductAsync();

        var result = await ReceiveOkAsync(Req(product, expiry: "04/1408"));

        Assert.Equal(new DateOnly(2029, 7, 21), result.Batch.ExpiryDate);
        Assert.Equal(ExpiryPrecision.Month, result.Batch.Precision);
        Assert.Equal(CalendarKind.Jalali, result.Batch.Calendar);
        Assert.Contains(ExpiryWarning.ConvertedFromJalali, result.Warnings);
        Assert.Contains(ExpiryWarning.MonthOnly, result.Warnings);
    }

    [Fact]
    public async Task RequiresExpiry_MissingExpiry_Returns400()
    {
        var product = await ProductAsync();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoExpiryNeeded_UndatedBatchAllowed()
    {
        var product = await ProductAsync(requiresExpiry: false);

        var result = await ReceiveOkAsync(Req(product, expiry: null));

        Assert.Null(result.Batch.ExpiryDate);
        Assert.Null(result.Batch.ExpiryText);
        Assert.False(result.Batch.IsExpired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task ZeroOrNegativeQuantity_Returns400(int quantity)
    {
        var product = await ProductAsync();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, quantity: quantity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidExpiry_Returns400WithMessage()
    {
        var product = await ProductAsync();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: "13/062028"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("تاریخ انقضا نامعتبر است", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Staff_CannotReceive()
    {
        var product = await ProductAsync();
        var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        var response = await staff.PostAsJsonAsync(ApiRoutes.Receipts, Req(product));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentReceipts_SameBatch_NoLostUpdate()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product, quantity: 5));
        var version = first.Batch.Version;

        var one = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null, quantity: 4, version: version));
        var two = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, expiry: null, quantity: 6, version: version));

        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, two.StatusCode);
        var detail = (await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ById(ApiRoutes.Products, product)))!;
        Assert.Equal(9, Assert.Single(detail.Batches).OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task BatchNumberFromPersianKeyboard_MatchesExisting()
    {
        var product = await ProductAsync();
        var first = await ReceiveOkAsync(Req(product, batch: "AB12", quantity: 2));

        var second = await ReceiveOkAsync(Req(product, batch: "شذ12", expiry: null, quantity: 3, version: first.Batch.Version));

        Assert.False(second.CreatedBatch);
        Assert.Equal(first.Batch.Id, second.Batch.Id);
        Assert.Equal(5, second.Batch.OnHand);
    }

    [Fact]
    public async Task InactiveProduct_Returns400()
    {
        var product = await ProductAsync();
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FirstAsync(p => p.Id == product)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EmptyBatchNumber_Returns400()
    {
        var product = await ProductAsync();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, Req(product, batch: "  "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NegativeUnitCost_Returns400()
    {
        var product = await ProductAsync();
        var request = Req(product);
        request.UnitCost = -1;

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownLocation_Returns400()
    {
        var product = await ProductAsync();
        var request = Req(product);
        request.LocationId = Guid.NewGuid();

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InactiveLocation_Returns400()
    {
        var product = await ProductAsync();
        var location = Guid.NewGuid();
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            db.StorageLocations.Add(new StorageLocation { Id = location, Name = "TEST-LOC", NameKey = "test-loc", ParentKey = "", IsActive = false });
            await db.SaveChangesAsync();
        }

        var request = Req(product);
        request.LocationId = location;
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.Receipts, request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
