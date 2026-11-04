using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class SessionTests : ApiTestBase
{
    [Fact]
    public async Task Me_WithoutLogin_Returns401()
    {
        using var client = App.NewClient();

        var response = await client.GetAsync(ApiRoutes.Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTheUserAndRole_AndSetsTheEmuCookie()
    {
        await App.AddAdminAsync("admin-a");
        using var client = App.NewClient();

        var response = await TestApp.LoginAsync(client, "admin-a", TestApp.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("PharmacyWarehouse.Auth=", StringComparison.Ordinal));

        var me = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.Equal("admin-a", me!.UserName);
        Assert.Equal([Roles.Admin], me.Roles);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        await App.AddAdminAsync("admin-a");
        using var client = App.NewClient();

        var response = await TestApp.LoginAsync(client, "admin-a", "wrong-password-1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_EndsTheSession()
    {
        await App.AddAdminAsync("admin-a");
        using var client = await LoggedInAsync("admin-a", TestApp.AdminPassword);

        var logout = await client.PostAsync(ApiRoutes.Logout, null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var me = await client.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task Post_WithoutCsrfHeader_Returns400()
    {
        using var client = App.NewClient(csrfHeader: false);

        var response = await TestApp.LoginAsync(client, "anyone", "anything");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FiveWrongPasswords_LockTheAccount_Returns429()
    {
        await App.AddTechnicianAsync("tech-a");
        using var client = App.NewClient();

        for (var i = 0; i < 4; i++)
        {
            var wrong = await TestApp.LoginAsync(client, "tech-a", "wrong-password-1");
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        // The fifth failure is the one that triggers the lockout; Identity may already answer it with 429.
        var fifth = await TestApp.LoginAsync(client, "tech-a", "wrong-password-1");
        Assert.True(fifth.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests);

        // Even the correct password is refused while locked.
        var correct = await TestApp.LoginAsync(client, "tech-a", TestApp.TechnicianPassword);
        Assert.Equal(HttpStatusCode.TooManyRequests, correct.StatusCode);
    }

    [Fact]
    public async Task Login_WithPersianDigitsInUsername_Succeeds()
    {
        await App.AddTechnicianAsync("1000007");
        using var client = App.NewClient();

        // 1000007 typed with Persian digits.
        var persian = "۱۰۰۰۰۰۷";
        var response = await TestApp.LoginAsync(client, persian, TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesOtherDeviceImmediately()
    {
        await App.AddTechnicianAsync("tech-a");
        using var deviceA = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);
        using var deviceB = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var change = await TestApp.ChangePasswordAsync(deviceA, TestApp.TechnicianPassword, TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        // No waiting: the security stamp is checked on every request.
        var other = await deviceB.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);

        // The device that changed the password stays logged in (RefreshSignInAsync).
        var same = await deviceA.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task InactiveUser_CannotLogIn_AndOpenSessionEndsImmediately()
    {
        var technician = await App.AddTechnicianAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        await App.SetIsActiveRawAsync(technician.Id, isActive: false);

        var me = await device.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);

        using var fresh = App.NewClient();
        var login = await TestApp.LoginAsync(fresh, "tech-a", TestApp.TechnicianPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    // The Api also serves the Client; a mistyped Api route must not come back as the Client's HTML page.
    [Fact]
    public async Task UnknownApiRoute_Returns404()
    {
        using var client = App.NewClient();

        var response = await client.GetAsync("/api/does-not-exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_WithoutLogin_ReturnsOk()
    {
        using var client = App.NewClient();

        var health = await client.GetFromJsonAsync<HealthResponse>(ApiRoutes.Health);

        Assert.Equal(HealthResponse.OkStatus, health!.Status);
    }
}
