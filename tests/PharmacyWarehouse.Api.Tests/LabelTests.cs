using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Tests;

public class LabelTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private async Task AddFactoryBarcodeAsync(Guid product, string code)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        db.ProductBarcodes.Add(new ProductBarcode { Id = Guid.NewGuid(), ProductId = product, Code = code, CodeKey = TextNormalizer.ScanKey(code)! });
        await db.SaveChangesAsync();
    }

    private async Task DeactivateAsync(Guid product)
    {
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        (await db.Products.FirstAsync(p => p.Id == product)).IsActive = false;
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> GenerateAsync(params Guid[] ids) =>
        Keeper.PostAsJsonAsync(ApiRoutes.LabelProductsGenerate, new GenerateLabelsRequest { ProductIds = ids.ToList() });

    private async Task<List<ProductLabelItem>> ListAsync() =>
        (await Keeper.GetFromJsonAsync<List<ProductLabelItem>>(ApiRoutes.LabelProducts))!;

    [Fact]
    public async Task List_ExcludesFactoryBarcodeAndInactive()
    {
        var plain = await App.AddProductRawAsync("TEST-DRUG-L1", null);
        var factory = await App.AddProductRawAsync("TEST-DRUG-L2", null);
        var inactive = await App.AddProductRawAsync("TEST-DRUG-L3", null);
        await AddFactoryBarcodeAsync(factory, "TEST0000000001");
        await DeactivateAsync(inactive);

        var item = Assert.Single(await ListAsync());

        Assert.Equal(plain, item.ProductId);
        Assert.Null(item.Code);
    }

    [Fact]
    public async Task Generate_AssignsSequentialCodes_AndIsIdempotent()
    {
        var a = await App.AddProductRawAsync("TEST-DRUG-L1", null);
        var b = await App.AddProductRawAsync("TEST-DRUG-L2", null);

        var response = await GenerateAsync(a, b);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<List<ProductLabelItem>>())!;
        Assert.Equal([InternalBarcode.Format(1), InternalBarcode.Format(2)], items.Select(i => i.Code));

        var again = (await (await GenerateAsync(a, b)).Content.ReadFromJsonAsync<List<ProductLabelItem>>())!;
        Assert.Equal(items.Select(i => i.Code), again.Select(i => i.Code));
    }

    [Fact]
    public async Task Generate_ContinuesAfterHighestExistingCode_AndCodeIsScannable()
    {
        var existing = await App.AddProductRawAsync("TEST-DRUG-L1", null);
        var next = await App.AddProductRawAsync("TEST-DRUG-L2", null);
        await AddFactoryBarcodeAsync(existing, InternalBarcode.Format(41));

        await GenerateAsync(next);

        var item = Assert.Single(await ListAsync(), i => i.ProductId == next);
        Assert.Equal(InternalBarcode.Format(42), item.Code);
        var found = await Keeper.GetFromJsonAsync<ProductDetail>(ApiRoutes.ProductByCode(item.Code!));
        Assert.Equal(next, found!.Summary.Id);
    }

    [Fact]
    public async Task Generate_SkipsFactoryBarcodeAndInactive()
    {
        var factory = await App.AddProductRawAsync("TEST-DRUG-L1", null);
        var inactive = await App.AddProductRawAsync("TEST-DRUG-L2", null);
        await AddFactoryBarcodeAsync(factory, "TEST0000000001");
        await DeactivateAsync(inactive);

        Assert.Equal(HttpStatusCode.OK, (await GenerateAsync(factory, inactive)).StatusCode);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(1, await db.ProductBarcodes.CountAsync());
    }

    [Fact]
    public async Task Generate_EmptyOrTooMany_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync()).StatusCode);
        var tooMany = Enumerable.Range(0, GenerateLabelsRequest.MaxProducts + 1).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(HttpStatusCode.BadRequest, (await GenerateAsync(tooMany)).StatusCode);
    }

    [Fact]
    public async Task Labels_RequireProductsManage()
    {
        await App.AddStaffAsync("staff-a");
        await App.AddManagerAsync("manager-a");

        foreach (var client in new[] { await LoggedInAsync("staff-a", TestApp.StaffPassword), await LoggedInAsync("manager-a", TestApp.StaffPassword) })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.LabelProducts)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(ApiRoutes.LabelProductsGenerate, new GenerateLabelsRequest { ProductIds = [Guid.NewGuid()] })).StatusCode);
        }
    }
}
