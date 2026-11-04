using System.Net;
using PharmacyWarehouse.Api.Users;

namespace PharmacyWarehouse.Api.Tests;

public class PasswordPolicyTests : ApiTestBase
{
    [Theory]
    [InlineData("123456789012")]                                                        // common
    [InlineData("۱۲۳۴۵۶۷۸۹۰۱۲")] // Persian digits
    [InlineData("aB3dE6g")]                                                             // 7 characters: too short
    [InlineData("12345678")]                                                            // common, 8 characters
    [InlineData("Password1")]                                                           // common, case-insensitive
    [InlineData("aaaaaaaaaaaa")]                                                        // too few distinct characters
    public async Task RejectedNewPassword_Returns422(string newPassword)
    {
        await App.AddTechnicianAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var response = await TestApp.ChangePasswordAsync(device, TestApp.TechnicianPassword, newPassword);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PasswordOf128Characters_IsAccepted_And129IsRejected()
    {
        await App.AddTechnicianAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var source = string.Concat(Enumerable.Repeat("abcdefghij", 13));

        var tooLong = await TestApp.ChangePasswordAsync(device, TestApp.TechnicianPassword, source[..129]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong.StatusCode);

        var maxLength = await TestApp.ChangePasswordAsync(device, TestApp.TechnicianPassword, source[..128]);
        Assert.Equal(HttpStatusCode.NoContent, maxLength.StatusCode);
    }

    [Fact]
    public async Task Technician_PasswordOf8Characters_IsAccepted()
    {
        await App.AddTechnicianAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var response = await TestApp.ChangePasswordAsync(device, TestApp.TechnicianPassword, "aB3dE6gH");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Admin_NeedsAtLeast8Characters()
    {
        await App.AddAdminAsync("admin-a");
        using var device = await LoggedInAsync("admin-a", TestApp.AdminPassword);

        var seven = await TestApp.ChangePasswordAsync(device, TestApp.AdminPassword, "aB3dE6g");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, seven.StatusCode);

        var eight = await TestApp.ChangePasswordAsync(device, TestApp.AdminPassword, "aB3dE6gH");
        Assert.Equal(HttpStatusCode.NoContent, eight.StatusCode);
    }

    // Generated initial passwords must always satisfy the current policy.
    [Fact]
    public async Task GeneratedPassword_AlwaysPassesThePolicy()
    {
        for (var i = 0; i < 50; i++)
        {
            var result = await App.ValidatePasswordAsync(CredentialsGenerator.NewPassword());

            Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(e => e.Code)));
        }
    }
}
