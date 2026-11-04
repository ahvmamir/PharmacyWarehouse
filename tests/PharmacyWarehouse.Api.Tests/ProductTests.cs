using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class ProductTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
    }

    internal static SaveProductRequest Req(string name, string? brand = null, string? pack = null, Guid? locationId = null, int? minStock = null) =>
        new() { Name = name, Brand = brand, PackDescription = pack, LocationId = locationId, MinStock = minStock };

    private async Task<ProductDetail> CreateAsync(SaveProductRequest request)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Products, request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDetail>())!;
    }

    private async Task<Guid> LocationAsync(string name, Guid? parent = null)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = name, ParentId = parent });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationItem>())!.Id;
    }

    private async Task<List<ProductListItem>> ListAsync(string query = "") =>
        (await Admin.GetFromJsonAsync<List<ProductListItem>>(ApiRoutes.Products + query))!;

    [Fact]
    public async Task Create_WithoutStock_ShowsZero()
    {
        var created = await CreateAsync(Req("TEST-DRUG-01"));

        var item = Assert.Single(await ListAsync());
        Assert.Equal(created.Summary.Id, item.Id);
        Assert.Equal(0, item.OnHand);
        Assert.Equal(0, item.UsableOnHand);
    }

    [Fact]
    public async Task DuplicateNameBrandPack_AfterNormalizing_Returns409()
    {
        await CreateAsync(Req("کو آموکسی کلاو 625", "TEST-BRAND", "TEST-PACK"));

        var response = await Admin.PostAsJsonAsync(ApiRoutes.Products, Req("کوآموکسی کلاو 625", "TEST-BRAND", "TEST-PACK"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SameName_DifferentPack_IsAllowed()
    {
        await CreateAsync(Req("TEST-DRUG-01", pack: "TEST-PACK-A"));

        await CreateAsync(Req("TEST-DRUG-01", pack: "TEST-PACK-B"));

        Assert.Equal(2, (await ListAsync()).Count);
    }

    [Fact]
    public async Task Staff_CanList_ButNotCreate()
    {
        await App.AddStaffAsync("staff-a");
        using var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(ApiRoutes.Products)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(ApiRoutes.Products, Req("TEST-DRUG-01"))).StatusCode);
    }

    [Fact]
    public async Task Search_FindsByBrandAndBarcode()
    {
        var a = await CreateAsync(Req("TEST-DRUG-01", "TEST-BRAND-X"));
        await CreateAsync(Req("TEST-DRUG-02", "TEST-BRAND-Y"));
        await Admin.PostAsJsonAsync(ApiRoutes.ProductBarcodes(a.Summary.Id), new AddBarcodeRequest { Code = "TEST0000000001" });

        var byBrand = await ListAsync("?q=brand-x");
        var byBarcode = await ListAsync("?q=TEST0000000001");

        Assert.Equal(a.Summary.Id, Assert.Single(byBrand).Id);
        Assert.Equal(a.Summary.Id, Assert.Single(byBarcode).Id);
    }

    [Fact]
    public async Task FilterByLocation_IncludesDescendants()
    {
        var root = await LocationAsync("TEST-LOC-ROOT");
        var child = await LocationAsync("TEST-LOC-CHILD", root);
        var other = await LocationAsync("TEST-LOC-OTHER");
        await CreateAsync(Req("TEST-DRUG-01", locationId: child));
        await CreateAsync(Req("TEST-DRUG-02", locationId: root));
        await CreateAsync(Req("TEST-DRUG-03", locationId: other));

        var items = await ListAsync($"?locationId={root}");

        Assert.Equal(["TEST-DRUG-01", "TEST-DRUG-02"], items.Select(i => i.Name).Order().ToList());
    }

    [Fact]
    public async Task FilterBelowMin_UsesUnexpiredStockOnly()
    {
        var product = await CreateAsync(Req("TEST-DRUG-01", minStock: 5));
        await App.AddBatchRawAsync(product.Summary.Id, "TEST-BATCH-01", null, onHand: 10, expiry: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30));

        var items = await ListAsync("?filter=belowMin");

        var item = Assert.Single(items);
        Assert.Equal(10, item.OnHand);
        Assert.Equal(0, item.UsableOnHand);
    }

    [Fact]
    public async Task Update_StaleVersion_Returns409()
    {
        var created = await CreateAsync(Req("TEST-DRUG-01"));
        var request = Req("TEST-DRUG-01", "TEST-BRAND");
        request.Version = created.Summary.Version;
        Assert.Equal(HttpStatusCode.OK, (await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Products, created.Summary.Id), request)).StatusCode);

        var stale = await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Products, created.Summary.Id), request);

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Fact]
    public async Task Delete_WithBatch_Returns409()
    {
        var product = await CreateAsync(Req("TEST-DRUG-01"));
        await App.AddBatchRawAsync(product.Summary.Id, "TEST-BATCH-01", null, onHand: 0);

        var response = await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Products, product.Summary.Id));
        var dependencies = await Admin.GetFromJsonAsync<DependencyList>(ApiRoutes.Dependencies(ApiRoutes.Products, product.Summary.Id));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(dependencies!.CanDelete);
        Assert.Equal(product.Summary.Id, Assert.Single(dependencies.Items).SubjectId);
    }

    [Fact]
    public async Task Delete_WithoutBatch_RemovesBarcodes_AndIsAudited()
    {
        var product = await CreateAsync(Req("TEST-DRUG-01"));
        var id = product.Summary.Id;
        var withBarcode = await (await Admin.PostAsJsonAsync(ApiRoutes.ProductBarcodes(id), new AddBarcodeRequest { Code = "TEST0000000001" }))
            .Content.ReadFromJsonAsync<ProductDetail>();
        var barcodeId = withBarcode!.Barcodes.Single().Id;

        var response = await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Products, id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync(ApiRoutes.ById(ApiRoutes.Products, id))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync(ApiRoutes.ProductByCode("TEST0000000001"))).StatusCode);
        Assert.Contains(await App.AuditRowsAsync(id), a => a.Action == "Deleted");
        Assert.Contains(await App.AuditRowsAsync(barcodeId), a => a.Action == "Deleted");
    }

    [Fact]
    public async Task TurnOnRequiresExpiry_WithUndatedBatch_KeepsBatch()
    {
        var created = await CreateAsync(new SaveProductRequest { Name = "TEST-DRUG-01", RequiresExpiry = false });
        var batchId = await App.AddBatchRawAsync(created.Summary.Id, "TEST-BATCH-01", null, onHand: 4);

        var response = await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Products, created.Summary.Id),
            new SaveProductRequest { Name = "TEST-DRUG-01", RequiresExpiry = true, Version = created.Summary.Version });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = await response.Content.ReadFromJsonAsync<ProductDetail>();
        Assert.True(detail!.RequiresExpiry);
        var batch = Assert.Single(detail.Batches);
        Assert.Equal(batchId, batch.Id);
        Assert.Null(batch.ExpiryDate);
        Assert.Equal(4, batch.OnHand);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task History_ListsChangesOfProductAndItsBarcodes()
    {
        var created = await CreateAsync(Req("TEST-DRUG-01"));
        var id = created.Summary.Id;
        await Admin.PostAsJsonAsync(ApiRoutes.ProductBarcodes(id), new AddBarcodeRequest { Code = "TEST0000000001" });
        var batchId = await App.AddBatchRawAsync(id, "TEST-BATCH-01", null, onHand: 3);

        var history = await Admin.GetFromJsonAsync<ProductHistory>(ApiRoutes.ProductHistory(id));

        Assert.Contains(history!.Changes, c => c.EntityType == "Product" && c.Action == "Created");
        Assert.Contains(history.Changes, c => c.EntityType == "ProductBarcode" && c.Action == "Created");
        Assert.Contains(history.Changes, c => c.EntityType == "Batch");
        var movement = Assert.Single(history.Movements);
        Assert.Equal(batchId, movement.BatchId);
        Assert.Equal("TEST-BATCH-01", movement.BatchNumber);
        Assert.Equal("سیستم", movement.UserName);
    }
}
