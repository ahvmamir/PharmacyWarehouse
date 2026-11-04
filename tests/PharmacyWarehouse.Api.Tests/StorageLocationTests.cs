using System.Net;
using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class StorageLocationTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
    }

    private async Task<StorageLocationItem> CreateAsync(string name, Guid? parentId = null, int sortOrder = 0)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations,
            new SaveStorageLocationRequest { Name = name, ParentId = parentId, SortOrder = sortOrder });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationItem>())!;
    }

    private Task<HttpResponseMessage> UpdateAsync(StorageLocationItem item, string? name = null, Guid? parentId = null, bool keepParent = true) =>
        Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Locations, item.Id), new SaveStorageLocationRequest
        {
            Name = name ?? item.Name,
            ParentId = keepParent ? item.ParentId : parentId,
            SortOrder = item.SortOrder,
            Version = item.Version
        });

    private async Task<StorageLocationItem> GetAsync(Guid id)
    {
        var all = await Admin.GetFromJsonAsync<List<StorageLocationItem>>(ApiRoutes.Locations);
        return all!.Single(l => l.Id == id);
    }

    [Fact]
    public async Task List_WithoutLogin_Returns401()
    {
        using var client = App.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Locations)).StatusCode);
    }

    [Fact]
    public async Task UserWithoutRole_GetsForbidden()
    {
        await App.AddUserWithoutRoleAsync("nobody");
        using var client = await LoggedInAsync("nobody", TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.Locations)).StatusCode);
        var create = await client.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = "TEST-LOC-01" });
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Create_Child_ReturnsFullPath()
    {
        var root = await CreateAsync("TEST-LOC-01");
        var child = await CreateAsync("TEST-LOC-02", root.Id);

        Assert.Equal("TEST-LOC-01", root.Path);
        Assert.Equal("TEST-LOC-01 › TEST-LOC-02", child.Path);
        Assert.Equal(root.Id, child.ParentId);
    }

    [Fact]
    public async Task EmptyName_Returns400()
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownParent_Returns400()
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations,
            new SaveStorageLocationRequest { Name = "TEST-LOC-01", ParentId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateNameAmongSiblings_AfterNormalizing_Returns409()
    {
        var root = await CreateAsync("ضلع جنوبی");
        await CreateAsync("قفسه ۲", root.Id);

        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations,
            new SaveStorageLocationRequest { Name = " قفسه 2 ", ParentId = root.Id });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateRootName_Returns409()
    {
        await CreateAsync("TEST-LOC-01");

        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = "test-loc-01" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SameName_UnderDifferentParents_IsAllowed()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var b = await CreateAsync("TEST-LOC-B");

        await CreateAsync("قفسه ۱", a.Id);
        await CreateAsync("قفسه ۱", b.Id);
    }

    [Fact]
    public async Task MoveUnderOwnDescendant_Returns409()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var b = await CreateAsync("TEST-LOC-B", a.Id);
        var c = await CreateAsync("TEST-LOC-C", b.Id);

        Assert.Equal(HttpStatusCode.Conflict, (await UpdateAsync(a, parentId: c.Id, keepParent: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateAsync(a, parentId: a.Id, keepParent: false)).StatusCode);
    }

    [Fact]
    public async Task RenameParent_ChildPathFollows()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var b = await CreateAsync("TEST-LOC-B", a.Id);
        var c = await CreateAsync("TEST-LOC-C", b.Id);

        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(b, name: "TEST-LOC-B2")).StatusCode);

        Assert.Equal("TEST-LOC-A › TEST-LOC-B2 › TEST-LOC-C", (await GetAsync(c.Id)).Path);
    }

    [Fact]
    public async Task MoveToRoot_Succeeds()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var b = await CreateAsync("TEST-LOC-B", a.Id);

        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(b, parentId: null, keepParent: false)).StatusCode);

        var moved = await GetAsync(b.Id);
        Assert.Null(moved.ParentId);
        Assert.Equal("TEST-LOC-B", moved.Path);
    }

    [Fact]
    public async Task TwoEditsWithSameVersion_SecondReturns409()
    {
        var a = await CreateAsync("TEST-LOC-A");

        Assert.Equal(HttpStatusCode.OK, (await UpdateAsync(a, name: "TEST-LOC-A1")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await UpdateAsync(a, name: "TEST-LOC-A2")).StatusCode);
    }

    [Fact]
    public async Task Delete_WithChildren_Returns409WithDependencies()
    {
        var a = await CreateAsync("TEST-LOC-A");
        await CreateAsync("TEST-LOC-B", a.Id);

        var dependencies = await Admin.GetFromJsonAsync<DependencyList>(ApiRoutes.Dependencies(ApiRoutes.Locations, a.Id));
        var delete = await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Locations, a.Id));

        Assert.False(dependencies!.CanDelete);
        Assert.Contains(dependencies.Items, d => d.Label == "TEST-LOC-B");
        Assert.Equal(HttpStatusCode.Conflict, delete.StatusCode);
    }

    [Fact]
    public async Task Delete_Leaf_Succeeds_AndIsAudited()
    {
        var a = await CreateAsync("TEST-LOC-A");

        Assert.Equal(HttpStatusCode.OK, (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Locations, a.Id))).StatusCode);

        var all = await Admin.GetFromJsonAsync<List<StorageLocationItem>>(ApiRoutes.Locations);
        Assert.DoesNotContain(all!, l => l.Id == a.Id);
        Assert.True(await App.CountAuditAsync(a.Id, "StorageLocation") >= 2);
    }

    [Fact]
    public async Task Deactivate_ThenActivate_FlipsFlag()
    {
        var a = await CreateAsync("TEST-LOC-A");

        var off = await Admin.PostAsJsonAsync(ApiRoutes.Deactivate(ApiRoutes.Locations, a.Id), new VersionRequest { Version = a.Version });
        var deactivated = (await off.Content.ReadFromJsonAsync<StorageLocationItem>())!;
        var on = await Admin.PostAsJsonAsync(ApiRoutes.Activate(ApiRoutes.Locations, a.Id), new VersionRequest { Version = deactivated.Version });
        var activated = (await on.Content.ReadFromJsonAsync<StorageLocationItem>())!;

        Assert.False(deactivated.IsActive);
        Assert.True(activated.IsActive);
        Assert.True(activated.Version > deactivated.Version);
    }
}
