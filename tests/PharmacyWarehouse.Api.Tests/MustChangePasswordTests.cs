using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// Creating users and resetting their passwords through the Api arrives in slice 8, along with its tests.
public class MustChangePasswordTests : ApiTestBase
{
    [Fact]
    public async Task FlaggedUser_SeesTheFlag()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        Assert.True((await GetMeAsync(device)).MustChangePassword);
    }

    [Fact]
    public async Task ChangePassword_ClearsTheFlag_AndStaysClearedAfterNewLogin()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var change = await TestApp.ChangePasswordAsync(device, TestApp.StaffPassword, TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, change.StatusCode);

        Assert.False((await GetMeAsync(device)).MustChangePassword);

        using var fresh = App.NewClient();
        var login = await TestApp.LoginAsync(fresh, "tech-a", TestApp.NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var me = await login.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.False(me!.MustChangePassword);
    }

    [Fact]
    public async Task RejectedChange_KeepsTheFlag()
    {
        await App.AddStaffAsync("tech-a", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-a", TestApp.StaffPassword);

        var change = await TestApp.ChangePasswordAsync(device, TestApp.StaffPassword, "abcdefg");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, change.StatusCode);

        Assert.True((await GetMeAsync(device)).MustChangePassword);
    }

    [Fact]
    public async Task Admin_FlagIsFalse()
    {
        await App.AddAdminAsync("admin-a");
        using var device = await LoggedInAsync("admin-a", TestApp.AdminPassword);

        Assert.False((await GetMeAsync(device)).MustChangePassword);
    }
}
