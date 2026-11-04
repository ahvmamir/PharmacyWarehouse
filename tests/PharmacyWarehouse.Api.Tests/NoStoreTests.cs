using System.Net;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class NoStoreTests : ApiTestBase
{
    private static void AssertNoStore(HttpResponseMessage response)
        => Assert.True(response.Headers.CacheControl?.NoStore, "Cache-Control: no-store is missing.");

    [Fact]
    public async Task SuccessfulResponse_IsNoStore()
    {
        using var client = App.NewClient();

        var response = await client.GetAsync(ApiRoutes.Health);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
    }

    [Fact]
    public async Task UnauthorizedResponse_IsNoStore()
    {
        using var client = App.NewClient();

        var response = await client.GetAsync(ApiRoutes.Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoStore(response);
    }

    [Fact]
    public async Task ResponseRejectedByCsrfCheck_IsNoStore()
    {
        using var client = App.NewClient(csrfHeader: false);

        var response = await TestApp.LoginAsync(client, "anyone", "anything");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertNoStore(response);
    }

    [Fact]
    public async Task LoginResponse_IsNoStore()
    {
        await App.AddAdminAsync("admin-a");
        using var client = App.NewClient();

        var response = await TestApp.LoginAsync(client, "admin-a", TestApp.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoStore(response);
    }
}
