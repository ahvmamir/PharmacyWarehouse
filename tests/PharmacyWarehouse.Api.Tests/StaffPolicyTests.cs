using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class StaffPolicyTests : ApiTestBase
{
    [Fact]
    public async Task Admin_PassesEveryPermission()
    {
        var admin = await App.AddAdminAsync("admin-a");

        foreach (var permission in Permissions.All)
        {
            Assert.True(await App.PassesPolicyAsync(admin.Id, permission.Key), permission.Key);
        }
    }

    // جدول بخش ۱۱ spec: نقش‌های پیش‌فرض دقیقاً همین مجوزها را دارند (migration اولیه آن‌ها را می‌کارد).
    public static TheoryData<string, string, bool> DefaultRolePermissions()
    {
        var granted = new Dictionary<string, string[]>
        {
            [Roles.Staff] = [Permissions.StockView, Permissions.WithdrawalCreate],
            [Roles.Keeper] =
            [
                Permissions.StockView, Permissions.WithdrawalCreate, Permissions.WithdrawalReview, Permissions.StockReceive,
                Permissions.StockAdjust, Permissions.ProductsManage, Permissions.DashboardView, Permissions.ReportsView,
                Permissions.DataImport, Permissions.DataExport, Permissions.BackupDownload
            ],
            [Roles.Manager] =
            [
                Permissions.StockView, Permissions.DashboardView, Permissions.ReportsView, Permissions.SettingsManage,
                Permissions.UsersManage, Permissions.RolesManage
            ]
        };

        var data = new TheoryData<string, string, bool>();
        foreach (var (role, permissions) in granted)
        {
            foreach (var permission in Permissions.All)
            {
                data.Add(role, permission.Key, permissions.Contains(permission.Key));
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DefaultRolePermissions))]
    public async Task DefaultRole_HasExactlySpecPermissions(string role, string permission, bool expected)
    {
        var user = await App.AddUserInRoleAsync($"user-{role.ToLowerInvariant()}", role);

        Assert.Equal(expected, await App.PassesPolicyAsync(user.Id, permission));
    }

    [Fact]
    public async Task UserWithoutRole_HasNothing()
    {
        var user = await App.AddUserWithoutRoleAsync("no-role");

        Assert.False(await App.PassesPolicyAsync(user.Id, Permissions.EquipmentView));
    }

    [Fact]
    public async Task InactiveAdmin_FailsEveryPermission()
    {
        var admin = await App.AddAdminAsync("admin-a");
        await App.SetIsActiveRawAsync(admin.Id, isActive: false);

        Assert.False(await App.PassesPolicyAsync(admin.Id, Permissions.EquipmentView));
    }

    [Fact]
    public async Task AdminWithInitialPassword_FailsEveryPermission()
    {
        var admin = await App.AddAdminAsync("admin-a", mustChangePassword: true);

        Assert.False(await App.PassesPolicyAsync(admin.Id, Permissions.EquipmentView));
    }

    [Fact]
    public async Task Me_ReturnsTheEffectivePermissions()
    {
        await App.AddTechnicianAsync("tech-a");
        using var device = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        var me = await GetMeAsync(device);

        Assert.Contains(Permissions.EquipmentManage, me.Permissions);
        Assert.DoesNotContain(Permissions.UsersManage, me.Permissions);
    }

    // نقش سفارشی و مجوز جداگانه‌ی کاربر

    private static async Task<HttpClient> UseAsync(HttpClient admin, Guid id, UpdateUserRequest request)
    {
        var response = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, id), request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return admin;
    }

    [Fact]
    public async Task CustomRole_GivesExactlyItsPermissions_AndCanBeChanged()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");

        var created = await admin.PostAsJsonAsync(ApiRoutes.Roles,
            new SaveRoleRequest { Name = "انباردار", Permissions = [Permissions.EquipmentView, Permissions.DataExport] });
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var role = await created.Content.ReadFromJsonAsync<RoleItem>();

        await UseAsync(admin, user.Id, new UpdateUserRequest { UserName = "tech-a", DisplayName = "tech-a", Role = role!.Name });

        Assert.True(await App.PassesPolicyAsync(user.Id, Permissions.DataExport));
        Assert.False(await App.PassesPolicyAsync(user.Id, Permissions.EquipmentManage));

        var edit = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Roles, role.Id),
            new SaveRoleRequest { Name = role.Name, Permissions = [Permissions.EquipmentManage] });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        Assert.True(await App.PassesPolicyAsync(user.Id, Permissions.EquipmentManage));
        Assert.False(await App.PassesPolicyAsync(user.Id, Permissions.DataExport));
    }

    [Fact]
    public async Task UserOverrides_BeatTheRole()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");

        await UseAsync(admin, user.Id, new UpdateUserRequest
        {
            UserName = "tech-a",
            DisplayName = "tech-a",
            Role = Roles.Technician,
            Grants = [Permissions.EquipmentDelete],
            Denies = [Permissions.EquipmentMove]
        });

        Assert.True(await App.PassesPolicyAsync(user.Id, Permissions.EquipmentDelete));
        Assert.False(await App.PassesPolicyAsync(user.Id, Permissions.EquipmentMove));
    }

    [Fact]
    public async Task SamePermission_CannotBeGrantedAndDenied()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");

        var response = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, user.Id), new UpdateUserRequest
        {
            UserName = "tech-a", DisplayName = "tech-a", Role = Roles.Technician,
            Grants = [Permissions.EquipmentMove], Denies = [Permissions.EquipmentMove]
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AdminRole_IsLocked()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var roles = await admin.GetFromJsonAsync<List<RoleItem>>(ApiRoutes.Roles);
        var adminRole = roles!.Single(r => r.Name == Roles.Admin);
        Assert.True(adminRole.IsLocked);

        var edit = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Roles, adminRole.Id),
            new SaveRoleRequest { Name = "x", Permissions = [] });
        Assert.Equal(HttpStatusCode.BadRequest, edit.StatusCode);

        var delete = await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Roles, adminRole.Id));
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task RoleWithUsers_ListsThem_AndIsNotDeleted_ThenDeletesWhenEmpty()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");
        var roles = await admin.GetFromJsonAsync<List<RoleItem>>(ApiRoutes.Roles);
        var technician = roles!.Single(r => r.Name == Roles.Technician);

        var dependencies = await admin.GetFromJsonAsync<DependencyList>(ApiRoutes.Dependencies(ApiRoutes.Roles, technician.Id));
        Assert.Contains(dependencies!.Items, i => i.Label == "tech-a");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Roles, technician.Id))).StatusCode);

        var created = await admin.PostAsJsonAsync(ApiRoutes.Roles, new SaveRoleRequest { Name = "empty-role", Permissions = [] });
        var empty = await created.Content.ReadFromJsonAsync<RoleItem>();
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Roles, empty!.Id))).StatusCode);
    }

    [Fact]
    public async Task ChangingAUsersRole_TakesEffectOnOpenSession()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");
        using var open = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);

        await UseAsync(admin, user.Id, new UpdateUserRequest { UserName = "tech-a", DisplayName = "tech-a", Role = Roles.Admin });

        Assert.Equal(HttpStatusCode.Unauthorized, (await open.GetAsync(ApiRoutes.Me)).StatusCode);

        using var fresh = await LoggedInAsync("tech-a", TestApp.TechnicianPassword);
        Assert.Contains(Roles.Admin, (await GetMeAsync(fresh)).Roles);
    }

    [Fact]
    public async Task UsernameChange_LogsInWithTheNewName()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");

        await UseAsync(admin, user.Id, new UpdateUserRequest { UserName = "tech-b", DisplayName = "tech-b", Role = Roles.Technician });

        using var client = App.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await TestApp.LoginAsync(client, "tech-a", TestApp.TechnicianPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TestApp.LoginAsync(client, "tech-b", TestApp.TechnicianPassword)).StatusCode);
    }

    [Fact]
    public async Task UsernameTakenByAnother_Returns409()
    {
        await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var user = await App.AddTechnicianAsync("tech-a");

        var response = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, user.Id),
            new UpdateUserRequest { UserName = "admin-a", DisplayName = "x", Role = Roles.Technician });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdminCannotChangeOwnRole_ButCanChangeOwnName()
    {
        var me = await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);

        var role = await admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Users, me.Id),
            new UpdateUserRequest { UserName = "admin-a", DisplayName = "x", Role = Roles.Technician });
        Assert.Equal(HttpStatusCode.BadRequest, role.StatusCode);

        await UseAsync(admin, me.Id, new UpdateUserRequest { UserName = "admin-a", DisplayName = "Renamed", Role = Roles.Admin });
    }

    [Fact]
    public async Task UserDelete_RawUserGoes_ActiveUserAndSelfStay()
    {
        var me = await App.AddAdminAsync("admin-a");
        using var admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
        var raw = await App.AddTechnicianAsync("tech-raw");

        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Users, me.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Users, raw.Id))).StatusCode);

        // کاربری که کاری کرده: ساخت یک واحد ردیف تاریخچه با نام او می‌سازد.
        var worker = await App.AddAdminAsync("admin-b");
        using var workerDevice = await LoggedInAsync("admin-b", TestApp.AdminPassword);
        (await workerDevice.PostAsJsonAsync(ApiRoutes.Units, new SaveUnitRequest { Name = "unit-x" })).EnsureSuccessStatusCode();

        var dependencies = await admin.GetFromJsonAsync<DependencyList>(ApiRoutes.Dependencies(ApiRoutes.Users, worker.Id));
        Assert.False(dependencies!.CanDelete);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Users, worker.Id))).StatusCode);
    }
}
