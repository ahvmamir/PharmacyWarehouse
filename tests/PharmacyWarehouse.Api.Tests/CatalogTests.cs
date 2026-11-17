using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// فهرست مرجع TTAC (بخش ۱۵.۴ spec): بارگذاری، جایگزینی، مجوز و جست‌وجو؛ فقط داده‌ی ساختگی.
public class CatalogTests : ApiTestBase
{
    private const string Header = "Irc,GTIN,GenericCode,DrugGenericName,BrandName,PersianBrandName,LicenseOwnerCompanyName,BrandOwnerCompanyName,ProducerCompanyName,ProducerCountryName,PackageCount,ConsumerPrice,IsTemporaryIRC";

    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-c");
        await App.AddStaffAsync("staff-c");
        Keeper = await LoggedInAsync("keeper-c", TestApp.StaffPassword);
    }

    private static string Row(string irc, string gtin, string brand, string persian, int pack = 30) =>
        $"{irc},{gtin},G1,TEST GENERIC,{brand},{persian},شرکت آزمایشی,شرکت آزمایشی,تولیدکننده‌ی آزمایشی,کشور آزمایشی,{pack},1000,FALSE";

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string csv)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(csv)]);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(file, "file", "test-catalog.csv");
        return await client.PostAsync(ApiRoutes.CatalogImport, content);
    }

    private async Task<CatalogImportResult> UploadOkAsync(string csv)
    {
        var response = await UploadAsync(Keeper, csv);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CatalogImportResult>())!;
    }

    private async Task<List<CatalogSuggestion>> SearchAsync(string q) => (await Keeper.GetFromJsonAsync<List<CatalogSuggestion>>(ApiRoutes.CatalogSearch(q)))!;

    [Fact]
    public async Task Import_ReportsCounts_AndStatus()
    {
        var csv = string.Join("\n", Header,
            Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا"),
            Row("TEST-0002", "00000000000018", "BAD", "نامعتبر"),
            Row("TEST-0003", "00000000000017", "TEST ALPHA2", "داروی آزمایشی آلفا ۲"),
            ",,,,,,,,,,,,", "");

        var result = await UploadOkAsync(csv);

        Assert.Equal(new CatalogImportResult(2, 1, 1, 1), result);
        var status = (await Keeper.GetFromJsonAsync<CatalogStatus>(ApiRoutes.CatalogStatus))!;
        Assert.Equal(2, status.Count);
        Assert.Equal("test-catalog.csv", status.LastFileName);
        Assert.NotNull(status.LastImportAtUtc);
    }

    [Fact]
    public async Task SecondImport_ReplacesTheWholeList()
    {
        await UploadOkAsync(Header + "\n" + Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا"));
        await UploadOkAsync(Header + "\n" + Row("TEST-0002", "00000000000024", "TEST BETA", "داروی آزمایشی بتا"));

        Assert.Empty(await SearchAsync("00000000000017"));
        Assert.Single(await SearchAsync("00000000000024"));
    }

    [Fact]
    public async Task MissingColumn_OrNoValidRow_IsValidationError_AndKeepsOldList()
    {
        await UploadOkAsync(Header + "\n" + Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا"));

        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(Keeper, "Irc,BrandName\nTEST-0009,X\n")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(Keeper, Header + "\n" + Row("TEST-0009", "123", "X", "ایکس"))).StatusCode);

        Assert.Single(await SearchAsync("00000000000017"));
    }

    [Fact]
    public async Task StaffWithoutPermission_IsForbidden()
    {
        var staff = await LoggedInAsync("staff-c", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(staff, Header)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ApiRoutes.CatalogSearch("TEST"))).StatusCode);
    }

    [Fact]
    public async Task Search_ByGtin13_Gtin14_AndGs1()
    {
        await UploadOkAsync(Header + "\n" + Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا", pack: 20));

        var hit = Assert.Single(await SearchAsync("00000000000017"));
        Assert.Equal("داروی آزمایشی آلفا", hit.Name);
        Assert.Equal("تولیدکننده‌ی آزمایشی", hit.Brand);
        Assert.Equal("20 عدد", hit.PackDescription);
        Assert.Single(await SearchAsync("0000000000017"));
        Assert.Single(await SearchAsync("(01)00000000000017(10)TEST-LOT(17)301231"));
    }

    [Fact]
    public async Task Search_ByPersianAndEnglishWords()
    {
        await UploadOkAsync(string.Join("\n", Header,
            Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا"),
            Row("TEST-0002", "00000000000024", "TEST BETA", "داروی آزمایشی بتا")));

        Assert.Single(await SearchAsync("آزمايشي بتا")); // ی عربی هم پیدا می‌شود
        Assert.Single(await SearchAsync("alpha"));
        Assert.Equal(2, (await SearchAsync("TEST GENERIC")).Count);
        Assert.Empty(await SearchAsync("ا"));
    }

    [Fact]
    public async Task Search_ReportsProductThatAlreadyHasTheGtin()
    {
        await UploadOkAsync(Header + "\n" + Row("TEST-0001", "00000000000017", "TEST ALPHA", "داروی آزمایشی آلفا"));
        var product = await App.AddProductRawAsync("TEST-DRUG-C1", null);
        (await Keeper.PostAsJsonAsync(ApiRoutes.ProductBarcodes(product), new AddBarcodeRequest { Code = "0000000000017" })).EnsureSuccessStatusCode();

        var hit = Assert.Single(await SearchAsync("00000000000017"));

        Assert.Equal(product, hit.ExistingProductId);
        Assert.Equal("TEST-DRUG-C1", hit.ExistingProductName);
    }
}
