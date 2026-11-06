using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// جست‌وجو، کد و allocation قلم برای stock.view یا withdrawal.create یا receipt.submit (بخش ۱۵.۲).
public class ProductLookupPolicyTests : ApiTestBase
{
    private async Task<(HttpClient Device, Guid Product)> TechWithoutStockViewAsync(string[] grants, string[] denies)
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddStaffAsync("tech-a");
        var update = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, user.Id), new UpdateUserRequest
        {
            UserName = "tech-a", DisplayName = "tech-a", Role = Roles.Staff, Grants = grants, Denies = denies
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var product = await App.AddProductRawAsync("TEST-DRUG-L1", null);
        await App.AddBatchRawAsync(product, "LOT-L1", null, onHand: 5, expiry: null);
        return (await LoggedInAsync("tech-a", TestApp.StaffPassword), product);
    }

    [Fact]
    public async Task StaffWithoutStockView_CanSearchLookUpByCodeAndAllocate()
    {
        var (device, product) = await TechWithoutStockViewAsync([], [Permissions.StockView]);

        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(ApiRoutes.ProductSearch("TEST-DRUG-L1"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await device.GetAsync(ApiRoutes.ProductByCode("NO-SUCH-CODE"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(ApiRoutes.ProductAllocation(product, 2))).StatusCode);

        // خود فهرست و جزئیات قلم هنوز stock.view می‌خواهد.
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.Products)).StatusCode);
    }

    [Fact]
    public async Task OnlyReceiptSubmit_CanSearchAndAllocate()
    {
        var (device, product) = await TechWithoutStockViewAsync(
            [Permissions.ReceiptSubmit], [Permissions.StockView, Permissions.WithdrawalCreate]);

        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(ApiRoutes.ProductSearch("TEST-DRUG-L1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(ApiRoutes.ProductAllocation(product, 1))).StatusCode);
    }

    [Fact]
    public async Task WithoutAnyOfTheThree_IsForbidden()
    {
        var (device, product) = await TechWithoutStockViewAsync(
            [], [Permissions.StockView, Permissions.WithdrawalCreate, Permissions.ReceiptSubmit]);

        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.ProductSearch("TEST-DRUG-L1"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.ProductByCode("X"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.ProductAllocation(product, 1))).StatusCode);
    }
}
