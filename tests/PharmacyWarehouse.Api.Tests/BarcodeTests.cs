using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class BarcodeTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
    }

    private async Task<Guid> ProductAsync(string name)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Products, ProductTests.Req(name));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductDetail>())!.Summary.Id;
    }

    private Task<HttpResponseMessage> AddAsync(HttpClient client, Guid id, string code) =>
        client.PostAsJsonAsync(ApiRoutes.ProductBarcodes(id), new AddBarcodeRequest { Code = code });

    [Fact]
    public async Task AddBarcode_ThenFindByCode()
    {
        var id = await ProductAsync("TEST-DRUG-01");

        var added = await AddAsync(Admin, id, "TEST0000000001");

        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var found = await Admin.GetFromJsonAsync<ProductDetail>(ApiRoutes.ProductByCode("TEST0000000001"));
        Assert.Equal(id, found!.Summary.Id);
        Assert.Equal("TEST0000000001", Assert.Single(found.Barcodes).Code);
    }

    [Fact]
    public async Task FindByCode_PersianKeyboardInput_FindsSameProduct()
    {
        var id = await ProductAsync("TEST-DRUG-01");
        await AddAsync(Admin, id, "AB12");

        var found = await Admin.GetFromJsonAsync<ProductDetail>(ApiRoutes.ProductByCode("شذ12"));

        Assert.Equal(id, found!.Summary.Id);
    }

    [Fact]
    public async Task DuplicateBarcode_OnOtherProduct_Returns409WithItsName()
    {
        var first = await ProductAsync("TEST-DRUG-01");
        var second = await ProductAsync("TEST-DRUG-02");
        await AddAsync(Admin, first, "TEST0000000001");

        var response = await AddAsync(Admin, second, "TEST0000000001");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("«TEST-DRUG-01»", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownCode_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync(ApiRoutes.ProductByCode("TEST9999999999"))).StatusCode);
    }

    [Fact]
    public async Task Staff_CannotAddBarcode()
    {
        var id = await ProductAsync("TEST-DRUG-01");
        await App.AddStaffAsync("staff-a");
        using var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await AddAsync(staff, id, "TEST0000000001")).StatusCode);
    }

    [Fact]
    public async Task RemoveBarcode_RemovesItFromLookup()
    {
        var id = await ProductAsync("TEST-DRUG-01");
        var detail = await (await AddAsync(Admin, id, "TEST0000000001")).Content.ReadFromJsonAsync<ProductDetail>();

        var removed = await Admin.DeleteAsync(ApiRoutes.ProductBarcode(id, detail!.Barcodes.Single().Id));

        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Admin.GetAsync(ApiRoutes.ProductByCode("TEST0000000001"))).StatusCode);
    }
}
