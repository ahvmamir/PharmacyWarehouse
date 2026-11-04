using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class UserTests : ApiTestBase
{
    private async Task<HttpClient> AdminAsync()
    {
        await App.AddAdminAsync("admin-a");
        return await LoggedInAsync("admin-a", TestApp.AdminPassword);
    }

    private static async Task<CreateUserResponse> CreateUserAsync(HttpClient admin, string username = "tech-new", string role = Roles.Technician)
    {
        var response = await admin.PostAsJsonAsync(ApiRoutes.Users,
            new CreateUserRequest { UserName = username, DisplayName = "Test User", Role = role });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<CreateUserResponse>())!;
    }

    [Fact]
    public async Task Technician_CannotReachUsers()
    {
        await App.AddTechnicianAsync("tech-a");
        using var technician = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await technician.GetAsync(ApiRoutes.Users)).StatusCode);

        var create = await technician.PostAsJsonAsync(ApiRoutes.Users,
            new CreateUserRequest { UserName = "x", DisplayName = "x", Role = Roles.Admin });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task NewUser_LogsInWithInitialPassword_AndMustChangeIt()
    {
        using var admin = await AdminAsync();

        var created = await CreateUserAsync(admin);
        Assert.True(created.InitialPassword.Length >= PasswordRules.MinLength);

        using var device = await LoggedInAsync("tech-new", created.InitialPassword);
        Assert.True((await GetMeAsync(device)).MustChangePassword);

        // Blocked from the app until the password is changed.
        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.Equipment)).StatusCode);

        var set = await device.PostAsJsonAsync(ApiRoutes.SetInitialPassword, new SetInitialPasswordRequest { NewPassword = TestApp.NewPassword });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(ApiRoutes.Equipment)).StatusCode);
    }

    [Fact]
    public async Task DuplicateUsername_Returns409()
    {
        using var admin = await AdminAsync();
        await CreateUserAsync(admin);

        var response = await admin.PostAsJsonAsync(ApiRoutes.Users,
            new CreateUserRequest { UserName = "tech-new", DisplayName = "Other", Role = Roles.Technician });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData("", "Name", Roles.Technician)]
    [InlineData("user-1", " ", Roles.Technician)]
    [InlineData("user-1", "Name", "Owner")]
    [InlineData("امیر", "Name", Roles.Technician)] // a Persian username is not typable on the login form's English keyboard
    public async Task InvalidNewUser_Returns400(string username, string displayName, string role)
    {
        using var admin = await AdminAsync();

        var response = await admin.PostAsJsonAsync(ApiRoutes.Users,
            new CreateUserRequest { UserName = username, DisplayName = displayName, Role = role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResetPassword_ReplacesPassword_KillsSessions_AndSetsTheFlag()
    {
        using var admin = await AdminAsync();
        var technician = await App.AddTechnicianAsync("tech-a");
        using var open = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var response = await admin.PostAsync(ApiRoutes.UserResetPassword(technician.Id), null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ResetPasswordResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, (await open.GetAsync(ApiRoutes.Me)).StatusCode);

        using var fresh = App.NewClient();
        var oldLogin = await TestApp.LoginAsync(fresh, "tech-a", TestApp.TechnicianPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

        using var device = await LoggedInAsync("tech-a", body!.NewPassword);
        Assert.True((await GetMeAsync(device)).MustChangePassword);
    }

    [Fact]
    public async Task Deactivate_EndsSessionNow_AndBlocksLogin_ActivateRestores()
    {
        using var admin = await AdminAsync();
        var technician = await App.AddTechnicianAsync("tech-a");
        using var open = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var off = await admin.PostAsync(ApiRoutes.Deactivate(ApiRoutes.Users, technician.Id), null);
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await open.GetAsync(ApiRoutes.Me)).StatusCode);

        using var fresh = App.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestApp.LoginAsync(fresh, "tech-a", TestApp.TechnicianPassword)).StatusCode);

        var on = await admin.PostAsync(ApiRoutes.Activate(ApiRoutes.Users, technician.Id), null);
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestApp.LoginAsync(fresh, "tech-a", TestApp.TechnicianPassword)).StatusCode);
    }

    [Fact]
    public async Task AdminCannotDeactivateOrResetThemselves()
    {
        using var admin = await AdminAsync();
        var me = await GetMeAsync(admin);

        var deactivate = await admin.PostAsync(ApiRoutes.Deactivate(ApiRoutes.Users, me.Id), null);
        var reset = await admin.PostAsync(ApiRoutes.UserResetPassword(me.Id), null);

        Assert.Equal(HttpStatusCode.BadRequest, deactivate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, reset.StatusCode);
    }

    [Fact]
    public async Task UserActions_WriteAuditRows()
    {
        using var admin = await AdminAsync();
        var created = await CreateUserAsync(admin);

        await admin.PostAsync(ApiRoutes.UserResetPassword(created.User.Id), null);
        await admin.PostAsync(ApiRoutes.Deactivate(ApiRoutes.Users, created.User.Id), null);

        Assert.Equal(3, await App.CountAuditAsync(created.User.Id, "User"));
    }

    [Fact]
    public async Task List_ShowsRoleAndState()
    {
        using var admin = await AdminAsync();
        await CreateUserAsync(admin);

        var users = await admin.GetFromJsonAsync<List<UserItem>>(ApiRoutes.Users);

        Assert.Contains(users!, u => u.UserName == "admin-a" && u.Role == Roles.Admin && !u.MustChangePassword);
        Assert.Contains(users!, u => u.UserName == "tech-new" && u.Role == Roles.Technician && u.MustChangePassword);
    }
}
