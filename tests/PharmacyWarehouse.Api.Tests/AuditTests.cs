using System.Net.Http.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;
using Microsoft.Data.Sqlite;

namespace PharmacyWarehouse.Api.Tests;

public class AuditTests : EquipmentTestBase
{
    [Fact]
    public async Task Create_WritesOneCreatedRow_WithTheRightUser()
    {
        var device = await CreateAsync(CaseInput());

        var row = Assert.Single(await HistoryAsync(Admin, device.Id));
        Assert.Equal("Created", row.Action);
        Assert.Equal("tech-a", row.UserName);
        Assert.Null(row.Field);
        Assert.Contains("TEST-0001", row.NewValue);
    }

    [Fact]
    public async Task Edit_WritesOneRowPerChangedField()
    {
        var device = await CreateAsync(CaseInput());

        var update = ToUpdate(device);
        update.Brand = "NewBrand";
        update.SerialNumber = "SN-1";
        var response = await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), update);
        response.EnsureSuccessStatusCode();

        var rows = (await HistoryAsync(Admin, device.Id)).Where(r => r.Action == "Updated").ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("admin-a", r.UserName));
        Assert.Single(rows.Select(r => r.ChangeSetId).Distinct());

        var brand = Assert.Single(rows, r => r.Field == "Brand");
        Assert.Null(brand.OldValue);
        Assert.Equal("NewBrand", brand.NewValue);
    }

    [Fact]
    public async Task EditWithoutChanges_WritesNoRows_ButStillAdvancesVersion()
    {
        var device = await CreateAsync(CaseInput());

        var response = await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), ToUpdate(device));
        var saved = await response.Content.ReadFromJsonAsync<EquipmentDetail>();

        Assert.Equal(device.Version + 1, saved!.Version);
        Assert.Single(await HistoryAsync(Admin, device.Id));
    }

    [Fact]
    public async Task ForeignKeyChange_StoresTheNameAtThatMoment()
    {
        var device = await CreateAsync(CaseInput());

        var update = ToUpdate(device);
        update.OwnerUnitId = UnitB.Id;
        (await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), update)).EnsureSuccessStatusCode();

        // A later rename must not rewrite the history.
        await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Units, UnitB.Id), new SaveUnitRequest { Name = "renamed", Version = UnitB.Version });

        var row = Assert.Single(await HistoryAsync(Admin, device.Id), r => r.Field == "OwnerUnitId");
        Assert.Equal("test-unit-a", AuditLabels.Value(row.Field, row.OldValue));
        Assert.Equal("test-unit-b", AuditLabels.Value(row.Field, row.NewValue));
    }

    [Fact]
    public async Task ComponentAddChangeRemove_AreInTheDeviceHistory()
    {
        var device = await CreateAsync(CaseInput());
        var route = ApiRoutes.ById(ApiRoutes.Equipment, device.Id);

        var add = ToUpdate(device);
        add.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "4GB" });
        var added = await (await Admin.PutAsJsonAsync(route, add)).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.Single(added!.Components);

        var change = ToUpdate(added);
        change.Components[0].Capacity = "8GB";
        var changed = await (await Admin.PutAsJsonAsync(route, change)).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.Equal(added.Components[0].Id, changed!.Components[0].Id);

        var remove = ToUpdate(changed);
        remove.Components.Clear();
        var removed = await (await Admin.PutAsJsonAsync(route, remove)).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.Empty(removed!.Components);

        var rows = (await HistoryAsync(Admin, device.Id)).Where(r => r.EntityType == "Component").ToList();

        var created = Assert.Single(rows, r => r.Action == "Created");
        Assert.Contains("4GB", created.NewValue);

        var updated = Assert.Single(rows, r => r.Action == "Updated");
        Assert.Equal("Capacity", updated.Field);
        Assert.Equal("Ram", updated.EntityLabel);
        Assert.Equal("4GB", updated.OldValue);
        Assert.Equal("8GB", updated.NewValue);

        var deleted = Assert.Single(rows, r => r.Action == "Deleted");
        Assert.Contains("8GB", deleted.OldValue);
    }

    [Fact]
    public async Task ComponentOnlyEdit_AdvancesTheDeviceVersion_SoAStaleEditIsRejected()
    {
        var device = await CreateAsync(CaseInput());
        var route = ApiRoutes.ById(ApiRoutes.Equipment, device.Id);

        var add = ToUpdate(device);
        add.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "4GB" });
        (await Admin.PutAsJsonAsync(route, add)).EnsureSuccessStatusCode();

        // Built from the old copy: it would silently delete the new RAM.
        var stale = await Technician.PutAsJsonAsync(route, ToUpdate(device));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Theory]
    [InlineData("UPDATE AuditEntries SET UserName = 'x'")]
    [InlineData("DELETE FROM AuditEntries")]
    public async Task DirectUpdateOrDelete_IsRejectedByTrigger(string sql)
    {
        await CreateAsync(CaseInput());

        var exception = await Assert.ThrowsAsync<SqliteException>(() => App.ExecuteRawAsync(sql));

        Assert.Contains("append-only", exception.Message);
    }
}
