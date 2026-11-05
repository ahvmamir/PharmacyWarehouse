using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class ProductSearchEndpointTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private async Task<ProductSearchResponse> SearchAsync(string text)
    {
        var response = await Keeper.GetAsync(ApiRoutes.ProductSearch(text));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductSearchResponse>())!;
    }

    [Fact]
    public async Task ExactBarcode_IsMarked_AndComesFirst()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی الف", null);
        await Keeper.PostAsJsonAsync(ApiRoutes.ProductBarcodes(product), new AddBarcodeRequest { Code = "TEST0000000001" });

        var result = await SearchAsync("test0000000001");

        Assert.Equal(product, result.ExactBarcodeId);
        Assert.Equal(product, result.Items[0].Id);
    }

    [Fact]
    public async Task PartialBarcodeOrName_IsNeverExact_EvenWithOneResult()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی الف", null);
        await Keeper.PostAsJsonAsync(ApiRoutes.ProductBarcodes(product), new AddBarcodeRequest { Code = "TEST0000000001" });

        var byPart = await SearchAsync("TEST00000");
        var byName = await SearchAsync("آزمایشی");

        Assert.Null(byPart.ExactBarcodeId);
        Assert.Equal(product, Assert.Single(byPart.Items).Id);
        Assert.Null(byName.ExactBarcodeId);
        Assert.Equal(product, Assert.Single(byName.Items).Id);
    }

    [Fact]
    public async Task InactiveProduct_OnlyComesWithExactBarcode()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی الف", null);
        await Keeper.PostAsJsonAsync(ApiRoutes.ProductBarcodes(product), new AddBarcodeRequest { Code = "TEST0000000001" });
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FindAsync(product))!.IsActive = false;
            await db.SaveChangesAsync();
        }

        Assert.Empty((await SearchAsync("آزمایشی")).Items);
        Assert.False(Assert.Single((await SearchAsync("TEST0000000001")).Items).IsActive);
    }

    [Fact]
    public async Task SpellingMistake_FindsProduct()
    {
        var product = await App.AddProductRawAsync("سفالکسین آزمایشی", null);

        Assert.Equal(product, Assert.Single((await SearchAsync("سفالکسن")).Items).Id);
    }

    [Fact]
    public async Task Empty_ReturnsNothing()
    {
        await App.AddProductRawAsync("داروی آزمایشی الف", null);

        Assert.Empty((await SearchAsync("  ")).Items);
    }

    [Fact]
    public async Task AnonymousRequest_Is401()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await App.NewClient().GetAsync(ApiRoutes.ProductSearch("x"))).StatusCode);
    }

    // باگ بخش ۱۵.۱: ردیف «ثبت» قلم شناسه‌ی خام محل نشان می‌داد.
    [Fact]
    public async Task History_CreatedRow_HasLocationName_ForNewAndLegacyRows()
    {
        Guid location;
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            var row = new StorageLocation { Id = Guid.NewGuid(), Name = "TEST-LOC-A", NameKey = "TEST-LOC-A", ParentKey = "" };
            db.StorageLocations.Add(row);
            await db.SaveChangesAsync();
            location = row.Id;
        }

        var product = await App.AddProductRawAsync("TEST-DRUG-01", location);
        using (var scope = App.CreateScope())
        {
            // ردیف قدیمی: همان شکلی که پیش از این اصلاح ذخیره می‌شد.
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.NewGuid(), ChangeSetId = Guid.NewGuid(), AtUtc = DateTime.UtcNow.AddDays(-1), UserName = "system",
                EntityType = nameof(Product), EntityId = product, SubjectId = product, Action = AuditActions.Created,
                NewValue = $"{{\"Name\":\"TEST-DRUG-01\",\"LocationId\":\"{location}\"}}"
            });
            await db.SaveChangesAsync();
        }

        var history = (await Keeper.GetFromJsonAsync<ProductHistory>(ApiRoutes.ProductHistory(product)))!;
        var created = history.Changes.Where(c => c.Action == AuditActions.Created).ToList();

        Assert.Equal(2, created.Count);
        Assert.All(created, c => Assert.Contains($"{location}|TEST-LOC-A", c.NewValue));
    }
}
