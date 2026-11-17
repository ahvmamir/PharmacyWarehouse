using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// بارکد دوبعدی GS1 (بخش ۹.۲ spec): قلم با GTIN پیدا می‌شود، چه بارکدش ۱۳ رقمی ثبت شده باشد چه ۱۴ رقمی.
// همه‌ی کدها ساختگی‌اند.
public class Gs1LookupTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    // GTIN ساختگی 0626000000001 + رقم کنترل ۹
    private const string Gtin = "06260000000019";
    private const string Ean13 = "6260000000019";
    private const string Uid = "99990000000000000001";
    private static readonly string Scan = $"01{Gtin}21{Uid}\u001D17280331\u001D10TESTB01";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-g");
        Keeper = await LoggedInAsync("keeper-g", TestApp.StaffPassword);
    }

    private async Task<Guid> ProductWithBarcodeAsync(string code)
    {
        var id = await App.AddProductRawAsync("TEST-DRUG-G1", null);
        var response = await Keeper.PostAsJsonAsync($"{ApiRoutes.Products}/{id}/barcodes", new AddBarcodeRequest { Code = code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return id;
    }

    [Theory]
    [InlineData(Ean13)]
    [InlineData(Gtin)]
    public async Task Gs1Scan_FindsProduct_ByEitherGtinForm(string stored)
    {
        var id = await ProductWithBarcodeAsync(stored);

        var search = (await Keeper.GetFromJsonAsync<ProductSearchResponse>(ApiRoutes.ProductSearch(Scan)))!;
        Assert.Equal(id, search.ExactBarcodeId);

        var byCode = await Keeper.GetAsync(ApiRoutes.ProductByCode(Scan));
        Assert.Equal(HttpStatusCode.OK, byCode.StatusCode);
    }

    [Fact]
    public async Task AddingGs1Scan_AsBarcode_StoresOnlyGtin()
    {
        var id = await App.AddProductRawAsync("TEST-DRUG-G2", null);

        var response = await Keeper.PostAsJsonAsync($"{ApiRoutes.Products}/{id}/barcodes", new AddBarcodeRequest { Code = Scan });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<ProductDetail>())!;
        Assert.Equal(Gtin, Assert.Single(detail.Barcodes).Code);
    }

    [Fact]
    public async Task Ean13AndGtin14_OfSameCode_CannotBelongToTwoProducts()
    {
        await ProductWithBarcodeAsync(Ean13);
        var other = await App.AddProductRawAsync("TEST-DRUG-G3", null);

        var response = await Keeper.PostAsJsonAsync($"{ApiRoutes.Products}/{other}/barcodes", new AddBarcodeRequest { Code = Gtin });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NewProductInReceipt_FromGs1Scan_StoresGtin()
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ReceiptsBulk, new BulkReceiptRequest
        {
            Lines =
            [
                new ReceiptRequest
                {
                    NewProduct = new SaveProductRequest { Name = "TEST-DRUG-G4" },
                    NewBarcode = Scan,
                    BatchNumber = "TESTB01",
                    Expiry = "31/03/2028",
                    Quantity = 1
                }
            ]
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var search = (await Keeper.GetFromJsonAsync<ProductSearchResponse>(ApiRoutes.ProductSearch(Ean13)))!;
        Assert.NotNull(search.ExactBarcodeId);
    }
}
