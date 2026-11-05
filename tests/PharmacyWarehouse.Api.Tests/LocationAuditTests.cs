using System.Net;
using System.Net.Http.Json;
using Microsoft.Data.Sqlite;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

// تاریخچه روی اولین موجودیت دامنه (محل)؛ همان رهگیر EF Core که همه‌ی جدول‌های IAudited از آن می‌گذرند.
public class LocationAuditTests : ApiTestBase
{
    private HttpClient Admin { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddAdminAsync("admin-a");
        Admin = await LoggedInAsync("admin-a", TestApp.AdminPassword);
    }

    private async Task<StorageLocationItem> CreateAsync(string name, Guid? parentId = null)
    {
        var response = await Admin.PostAsJsonAsync(ApiRoutes.Locations, new SaveStorageLocationRequest { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationItem>())!;
    }

    private async Task<StorageLocationItem> PutAsync(StorageLocationItem item, string name, Guid? parentId)
    {
        var response = await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Locations, item.Id),
            new SaveStorageLocationRequest { Name = name, ParentId = parentId, SortOrder = item.SortOrder, Version = item.Version });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<StorageLocationItem>())!;
    }

    [Fact]
    public async Task Create_StoresPersianTextUnescaped()
    {
        var location = await CreateAsync("قفسه‌ی آزمایشی");

        var row = Assert.Single(await App.AuditRowsAsync(location.Id));

        Assert.Contains("قفسه‌ی آزمایشی", row.NewValue);
        Assert.DoesNotContain("\\u", row.NewValue);
    }

    [Fact]
    public async Task Create_WritesOneCreatedRow_WithTheRightUser()
    {
        var location = await CreateAsync("TEST-LOC-01");

        var row = Assert.Single(await App.AuditRowsAsync(location.Id));
        Assert.Equal("Created", row.Action);
        Assert.Equal("admin-a", row.UserName);
        Assert.Equal("StorageLocation", row.EntityType);
        Assert.Contains("TEST-LOC-01", row.NewValue);
    }

    [Fact]
    public async Task Rename_WritesOneUpdatedRow_WithOldAndNewName()
    {
        var location = await CreateAsync("TEST-LOC-01");

        await PutAsync(location, "TEST-LOC-02", null);

        var row = Assert.Single(await App.AuditRowsAsync(location.Id), r => r.Action == "Updated");
        Assert.Equal("Name", row.Field);
        Assert.Equal("TEST-LOC-01", row.OldValue);
        Assert.Equal("TEST-LOC-02", row.NewValue);
    }

    [Fact]
    public async Task DerivedKeyColumns_AreNotAudited()
    {
        var location = await CreateAsync("TEST-LOC-01");

        await PutAsync(location, "TEST-LOC-02", null);

        var updates = (await App.AuditRowsAsync(location.Id)).Where(r => r.Action == "Updated").ToList();
        var row = Assert.Single(updates);
        Assert.Equal("Name", row.Field);
    }

    [Fact]
    public async Task SaveWithoutChanges_WritesNoRows_ButAdvancesVersion()
    {
        var location = await CreateAsync("TEST-LOC-01");

        var saved = await PutAsync(location, location.Name, null);

        Assert.Equal(location.Version + 1, saved.Version);
        Assert.Single(await App.AuditRowsAsync(location.Id));
    }

    [Fact]
    public async Task Move_StoresTheParentNameAtThatMoment()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var b = await CreateAsync("TEST-LOC-B");
        var child = await CreateAsync("TEST-LOC-C", a.Id);

        await PutAsync(child, child.Name, b.Id);

        var row = Assert.Single(await App.AuditRowsAsync(child.Id), r => r.Field == "ParentId");
        Assert.Equal($"{a.Id}|TEST-LOC-A", row.OldValue);
        Assert.Equal($"{b.Id}|TEST-LOC-B", row.NewValue);
    }

    [Fact]
    public async Task OneRequest_IsOneChangeSet()
    {
        var a = await CreateAsync("TEST-LOC-A");
        var child = await CreateAsync("TEST-LOC-C");

        await PutAsync(child, "TEST-LOC-C2", a.Id);

        var updates = (await App.AuditRowsAsync(child.Id)).Where(r => r.Action == "Updated").ToList();
        Assert.Equal(2, updates.Count);
        Assert.Single(updates.Select(r => r.ChangeSetId).Distinct());
    }

    [Theory]
    [InlineData("UPDATE AuditEntries SET UserName = 'x'")]
    [InlineData("DELETE FROM AuditEntries")]
    public async Task DirectUpdateOrDelete_IsRejectedByTrigger(string sql)
    {
        await CreateAsync("TEST-LOC-01");

        var exception = await Assert.ThrowsAsync<SqliteException>(() => App.ExecuteRawAsync(sql));

        Assert.Contains("append-only", exception.Message);
    }

    // پایگاه‌داده‌ی تازه از migration ساخته می‌شود؛ پس triggerها باید از آن آمده باشند.
    [Theory]
    [InlineData("AuditEntries_NoUpdate")]
    [InlineData("AuditEntries_NoDelete")]
    public async Task Triggers_ExistAfterFreshMigration(string trigger)
    {
        var count = await App.CountSqliteMasterAsync("trigger", trigger);

        Assert.Equal(1, count);
    }
}
