using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class SetInitialPasswordTests : ApiTestBase
{
    private static Task<HttpResponseMessage> SetAsync(HttpClient client, string newPassword)
        => client.PostAsJsonAsync(
            ApiRoutes.SetInitialPassword, new SetInitialPasswordRequest { NewPassword = newPassword });

    [Fact]
    public async Task FlaggedUser_CanSetPasswordWithoutCurrent_AndFlagIsCleared()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(device, TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The device that set the password stays logged in (RefreshSignInAsync).
        Assert.False((await GetMeAsync(device)).MustChangePassword);

        using var fresh = App.NewClient();
        var oldPassword = await TestApp.LoginAsync(fresh, "tech-a", TestApp.StaffPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldPassword.StatusCode);

        var newPassword = await TestApp.LoginAsync(fresh, "tech-a", TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task SetInitialPassword_InvalidatesOtherDeviceImmediately()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var deviceA = await LoggedInAsync("tech-a", TestApp.StaffPassword);
        using var deviceB = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(deviceA, TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var other = await deviceB.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);

        var same = await deviceA.GetAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);
    }

    [Fact]
    public async Task NotFlaggedUser_Returns403_AndPasswordIsUnchanged()
    {
        await App.AddStaffAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(device, TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var fresh = App.NewClient();
        var login = await TestApp.LoginAsync(fresh, "tech-a", TestApp.StaffPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task NotFlaggedAdmin_Returns403()
    {
        await App.AddAdminAsync("admin-a");
        using var device = await LoggedInAsync("admin-a", TestApp.AdminPassword);

        var response = await SetAsync(device, "aB3dE6gH9jKm");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("123456789012")]                                                        // common
    [InlineData("۱۲۳۴۵۶۷۸۹۰۱۲")] // Persian digits
    [InlineData("aB3dE6g")]                                                         // 7 characters: too short
    [InlineData("aaaaaaaaaaaa")]                                                        // too few distinct characters
    public async Task RejectedPassword_Returns422_AndKeepsFlagAndOldPassword(string newPassword)
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(device, newPassword);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        Assert.True((await GetMeAsync(device)).MustChangePassword);

        using var fresh = App.NewClient();
        var login = await TestApp.LoginAsync(fresh, "tech-a", TestApp.StaffPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task SameAsInitialPassword_Returns422()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(device, TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.True((await GetMeAsync(device)).MustChangePassword);
    }

    [Fact]
    public async Task WithoutLogin_Returns401()
    {
        using var client = App.NewClient();

        var response = await SetAsync(client, TestApp.NewPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task EmptyPassword_Returns400()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var response = await SetAsync(device, "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
