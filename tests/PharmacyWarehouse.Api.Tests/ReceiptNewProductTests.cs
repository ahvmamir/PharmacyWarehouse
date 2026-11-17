using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

// تعریف قلم تازه داخل ورود جنس (بخش ۱۵.۳ spec): قلم، بارکد، بچ و حرکت در یک تراکنش؛ همه یا هیچ.
public class ReceiptNewProductTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-n");
        Keeper = await LoggedInAsync("keeper-n", TestApp.StaffPassword);
    }

    private static ReceiptRequest NewLine(string name, string batch = "TEST-BATCH-N1", string? barcode = null, int quantity = 3, string? brand = null) => new()
    {
        NewProduct = new SaveProductRequest { Name = name, Brand = brand, PackDescription = "TEST-PACK-30" },
        NewBarcode = barcode,
        BatchNumber = batch,
        Expiry = "15/03/2030",
        Quantity = quantity
    };

    private Task<HttpResponseMessage> PostAsync(HttpClient client, params ReceiptRequest[] lines) =>
        client.PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest { Lines = lines.ToList() });

    private async Task<T> DbAsync<T>(Func<WarehouseDbContext, Task<T>> read)
    {
        using var scope = App.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<WarehouseDbContext>());
    }

    private static async Task<Dictionary<string, string[]>> ErrorsAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Errors;

    private sealed class ProblemBody
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }

    [Fact]
    public async Task NewProduct_IsCreated_WithBarcodeBatchAndMovement()
    {
        var response = await PostAsync(Keeper, NewLine("TEST-DRUG-N1", barcode: "TEST-0000000001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var line = Assert.Single((await response.Content.ReadFromJsonAsync<BulkReceiptResult>())!.Lines);
        Assert.True(line.CreatedBatch);
        Assert.Equal(3, line.Batch.OnHand);

        var product = await DbAsync(db => db.Products.SingleAsync(p => p.Name == "TEST-DRUG-N1"));
        Assert.True(product.IsActive);
        Assert.False(product.IsProvisional);
        Assert.Equal("TEST-PACK-30", product.PackDescription);
        Assert.Equal(product.Id, await DbAsync(db => db.ProductBarcodes.Where(b => b.Code == "TEST-0000000001").Select(b => b.ProductId).SingleAsync()));
        Assert.Contains("TEST-0000000001", product.SearchText);

        var lookup = await Keeper.GetAsync(ApiRoutes.ProductByCode("TEST-0000000001"));
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task SameNewProduct_InTwoLines_CreatesOneProduct_TwoBatches()
    {
        var response = await PostAsync(Keeper,
            NewLine("TEST-DRUG-N2", "TEST-BATCH-N1", barcode: "TEST-0000000002"),
            NewLine("TEST-DRUG-N2", "TEST-BATCH-N2", barcode: "TEST-0000000002", quantity: 2));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await DbAsync(db => db.Products.CountAsync(p => p.Name == "TEST-DRUG-N2")));
        var productId = await DbAsync(db => db.Products.Where(p => p.Name == "TEST-DRUG-N2").Select(p => p.Id).SingleAsync());
        Assert.Equal(2, await DbAsync(db => db.Batches.CountAsync(b => b.ProductId == productId)));
        Assert.Equal(1, await DbAsync(db => db.ProductBarcodes.CountAsync(b => b.ProductId == productId)));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task NewProduct_DuplicatingExisting_Conflicts_OnThatLine()
    {
        await App.AddProductRawAsync("TEST-DRUG-N3", null);
        var request = NewLine("TEST-DRUG-N3");
        request.NewProduct!.PackDescription = null;

        var response = await PostAsync(Keeper, request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(0, "NewProduct.Name"), (await ErrorsAsync(response)).Keys);
    }

    [Fact]
    public async Task NewBarcode_OwnedByAnotherProduct_Conflicts()
    {
        await PostAsync(Keeper, NewLine("TEST-DRUG-N4", barcode: "TEST-0000000004"));

        var response = await PostAsync(Keeper, NewLine("TEST-DRUG-N5", barcode: "TEST-0000000004"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(0, "NewBarcode"), (await ErrorsAsync(response)).Keys);
        Assert.Equal(0, await DbAsync(db => db.Products.CountAsync(p => p.Name == "TEST-DRUG-N5")));
    }

    [Fact]
    public async Task InvalidOtherLine_SavesNothing_NotEvenTheNewProduct()
    {
        var bad = NewLine("TEST-DRUG-N7", "TEST-BATCH-N7");
        bad.Quantity = 0;

        var response = await PostAsync(Keeper, NewLine("TEST-DRUG-N6"), bad);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await DbAsync(db => db.Products.CountAsync(p => p.Name.StartsWith("TEST-DRUG-N"))));
    }

    [Fact]
    public async Task NewProduct_WithoutName_IsValidationError()
    {
        var response = await PostAsync(Keeper, NewLine("   "));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(0, "NewProduct.Name"), (await ErrorsAsync(response)).Keys);
    }

    [Fact]
    public async Task ReceiverWithoutProductsManage_CannotDefineProducts()
    {
        // نقش Staff فقط برای این تست ورود جنس می‌گیرد، ولی تعریف قلم نه.
        await App.ExecuteRawAsync("INSERT INTO AspNetRoleClaims (RoleId, ClaimType, ClaimValue) SELECT Id, 'perm', 'stock.receive' FROM AspNetRoles WHERE NormalizedName = 'STAFF';");
        await App.AddStaffAsync("staff-n");
        using var staff = await LoggedInAsync("staff-n", TestApp.StaffPassword);

        var response = await PostAsync(staff, NewLine("TEST-DRUG-N8"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(BulkReceiptErrors.Key(0, "NewProduct"), (await ErrorsAsync(response)).Keys);
        Assert.Equal(0, await DbAsync(db => db.Products.CountAsync(p => p.Name == "TEST-DRUG-N8")));
    }

    [Fact]
    public async Task NewProductCreation_IsAudited_WithProductName_OnBatch()
    {
        await PostAsync(Keeper, NewLine("TEST-DRUG-N9"));
        var productId = await DbAsync(db => db.Products.Where(p => p.Name == "TEST-DRUG-N9").Select(p => p.Id).SingleAsync());
        var batchId = await DbAsync(db => db.Batches.Where(b => b.ProductId == productId).Select(b => b.Id).SingleAsync());

        Assert.NotEmpty(await App.AuditRowsAsync(productId));
        var batchCreated = Assert.Single(await App.AuditRowsAsync(batchId), r => r.Action == "Created");
        Assert.Contains("TEST-DRUG-N9", batchCreated.NewValue);
    }
}
