using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class EquipmentTests : EquipmentTestBase
{
    private static string ListRoute(string query) => $"{ApiRoutes.Equipment}?{query}";

    private async Task<PagedResult<EquipmentListItem>> ListAsync(string query = "") =>
        (await Technician.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ListRoute(query)))!;

    [Fact]
    public async Task WithoutLogin_Returns401()
    {
        using var client = App.NewClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(ApiRoutes.Equipment)).StatusCode);
    }

    [Fact]
    public async Task UserWithInitialPassword_Returns403()
    {
        await App.AddTechnicianAsync("tech-new", mustChangePassword: true);
        using var device = await LoggedInAsync("tech-new", TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await device.GetAsync(ApiRoutes.Equipment)).StatusCode);
    }

    // یکتایی

    [Theory]
    [InlineData("۱۲۳۴")]
    [InlineData("1234")]
    [InlineData(" 1234 ")]
    public async Task DuplicateAssetNumber_InAnyForm_Returns409WithTheExistingDevice(string assetNumber)
    {
        var existing = await CreateAsync(CaseInput("1234"));

        var response = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, CaseInput(assetNumber));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ProblemTypes.DuplicateAssetNumber, body.RootElement.GetProperty("type").GetString());
        Assert.Equal(existing.Id, body.RootElement.GetProperty("existingId").GetGuid());
    }

    [Fact]
    public async Task DuplicateOfARetiredDevice_AlsoReturns409()
    {
        var existing = await CreateAsync(CaseInput("1234"));
        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(existing.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = existing.Version });

        var response = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, CaseInput("1234"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DevicesWithoutAssetNumber_GetDifferentTemporaryNumbers()
    {
        var first = await CreateAsync(CaseInput(null));
        var second = await CreateAsync(CaseInput(null));

        Assert.True(first.HasTemporaryAssetNumber);
        Assert.Equal("TEMP-000001", first.AssetNumber);
        Assert.Equal("TEMP-000002", second.AssetNumber);
    }

    [Fact]
    public async Task TemporaryNumber_StaysOnEdit_AndIsReplacedByARealOne()
    {
        var device = await CreateAsync(CaseInput(null));
        var route = ApiRoutes.ById(ApiRoutes.Equipment, device.Id);

        var kept = await (await Technician.PutAsJsonAsync(route, ToUpdate(device))).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.Equal(device.AssetNumber, kept!.AssetNumber);

        var real = ToUpdate(kept);
        real.UseTemporaryAssetNumber = false;
        real.AssetNumber = "REAL-1";
        var saved = await (await Technician.PutAsJsonAsync(route, real)).Content.ReadFromJsonAsync<EquipmentDetail>();

        Assert.Equal("REAL-1", saved!.AssetNumber);
        Assert.False(saved.HasTemporaryAssetNumber);
    }

    [Fact]
    public async Task UpdatedAt_MovesOnARealChangeOnly()
    {
        var device = await CreateAsync(CaseInput());
        Assert.NotEqual(default, device.UpdatedAtUtc);
        var route = ApiRoutes.ById(ApiRoutes.Equipment, device.Id);

        var same = await (await Technician.PutAsJsonAsync(route, ToUpdate(device))).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.Equal(device.UpdatedAtUtc, same!.UpdatedAtUtc);

        await Task.Delay(20);
        var change = ToUpdate(same);
        change.Brand = "Changed";
        var changed = await (await Technician.PutAsJsonAsync(route, change)).Content.ReadFromJsonAsync<EquipmentDetail>();
        Assert.True(changed!.UpdatedAtUtc > same.UpdatedAtUtc);

        var item = (await ListAsync()).Items.Single();
        Assert.Equal(changed.UpdatedAtUtc, item.UpdatedAtUtc);
    }

    [Fact]
    public async Task MouseAndKeyboardModels_AreSaved()
    {
        var input = CaseInput();
        input.HasMouse = true;
        input.MouseModel = "M100";
        input.HasKeyboard = true;
        input.KeyboardModel = "K100";

        var device = await CreateAsync(input);

        Assert.Equal("M100", device.MouseModel);
        Assert.Equal("K100", device.KeyboardModel);
    }

    [Fact]
    public async Task Create_HubWithChildren_LinksAndMovesThem()
    {
        var monitor = MonitorInput("A2");
        monitor.FloorId = FloorB.Id;
        var child = await CreateAsync(monitor);

        var hub = CaseInput("A1");
        hub.LinkChildIds.Add(child.Id);
        var created = await CreateAsync(hub);

        Assert.Equal(child.Id, Assert.Single(created.Children).Id);

        var fresh = await GetAsync(Technician, child.Id);
        Assert.Equal(created.Id, fresh.Parent!.Id);
        Assert.Equal(FloorA.Id, fresh.FloorId);

        // The hub did not exist in the database yet when the link was audited; history must still name it.
        var row = Assert.Single(await HistoryAsync(Technician, child.Id), r => r.Field == "ParentId");
        Assert.Equal("A1", PharmacyWarehouse.Domain.Assets.AuditLabels.Value(row.Field, row.NewValue));
    }

    [Fact]
    public async Task Create_HubWithChildren_CanKeepTheirLocation()
    {
        var monitor = MonitorInput("A2");
        monitor.FloorId = FloorB.Id;
        var child = await CreateAsync(monitor);

        var hub = CaseInput("A1");
        hub.LinkChildIds.Add(child.Id);
        hub.MoveLinkedToSameLocation = false;
        await CreateAsync(hub);

        Assert.Equal(FloorB.Id, (await GetAsync(Technician, child.Id)).FloorId);
    }

    [Fact]
    public async Task Create_NonHubWithHub_LinksIt()
    {
        var hub = await CreateAsync(CaseInput("A1"));

        var monitor = MonitorInput("A2");
        monitor.LinkParentId = hub.Id;
        var created = await CreateAsync(monitor);

        Assert.Equal(hub.Id, created.Parent!.Id);
    }

    [Fact]
    public async Task Create_WithAChildAlreadyLinked_Returns400_AndSavesNothing()
    {
        var hub = await CreateAsync(CaseInput("A1"));
        var monitor = MonitorInput("A2");
        monitor.LinkParentId = hub.Id;
        var child = await CreateAsync(monitor);

        var second = CaseInput("A3");
        second.LinkChildIds.Add(child.Id);
        var response = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, second);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(hub.Id, (await GetAsync(Technician, child.Id)).Parent!.Id);
        Assert.Equal(0, (await ListAsync("q=A3")).Total);
    }

    [Fact]
    public async Task EditingOwnAssetNumber_IsNotADuplicate()
    {
        var device = await CreateAsync(CaseInput("1234"));

        var response = await Technician.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), ToUpdate(device));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DuplicateComputerName_IsOnlyAWarning()
    {
        var first = CaseInput("A1");
        first.ComputerName = "TEST-PC";
        var created = await CreateAsync(first);
        Assert.Empty(created.Warnings);

        var second = CaseInput("A2");
        second.ComputerName = " test-pc ";
        var duplicate = await CreateAsync(second);

        Assert.Single(duplicate.Warnings);
    }

    // اعتبارسنجی

    [Fact]
    public async Task InvalidInput_Returns400_WithFieldErrors()
    {
        var input = MonitorInput();
        input.Brand = null;
        input.SizeText = "5";

        var response = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Brand", out _));
        Assert.True(errors.TryGetProperty("SizeText", out _));
    }

    [Fact]
    public async Task FloorOfAnotherBuilding_Returns400()
    {
        var other = await PostOkAsync<BuildingItem>(Admin, ApiRoutes.Buildings, new SaveBuildingRequest { Name = "other" });
        var input = CaseInput();
        input.BuildingId = other.Id;

        var response = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, input);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InactiveUnit_RejectedWhenChosen_ButKeptOnExistingDevice()
    {
        var device = await CreateAsync(CaseInput("A1"));
        await PostOkAsync<UnitItem>(Admin, ApiRoutes.Deactivate(ApiRoutes.Units, UnitA.Id), new VersionRequest { Version = UnitA.Version });

        var newDevice = await Technician.PostAsJsonAsync(ApiRoutes.Equipment, CaseInput("A2"));
        Assert.Equal(HttpStatusCode.BadRequest, newDevice.StatusCode);

        var edit = await Technician.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), ToUpdate(device));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
    }

    [Fact]
    public async Task Edit_CannotChangeTypeLocationOrStatus()
    {
        var device = await CreateAsync(CaseInput());

        var update = ToUpdate(device);
        update.Type = EquipmentType.Laptop;
        update.FloorId = FloorB.Id;
        update.LocationUnitId = UnitB.Id;
        update.Room = "changed";

        var response = await Technician.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), update);
        var saved = await response.Content.ReadFromJsonAsync<EquipmentDetail>();

        Assert.Equal(EquipmentType.Case, saved!.Type);
        Assert.Equal(FloorA.Id, saved.FloorId);
        Assert.Equal(UnitA.Id, saved.LocationUnitId);
        Assert.Null(saved.Room);
    }

    [Fact]
    public async Task TwoEditsWithSameVersion_SecondReturns409()
    {
        var device = await CreateAsync(CaseInput());
        var route = ApiRoutes.ById(ApiRoutes.Equipment, device.Id);

        var first = ToUpdate(device);
        first.Brand = "One";
        var second = ToUpdate(device);
        second.Brand = "Two";

        Assert.Equal(HttpStatusCode.OK, (await Technician.PutAsJsonAsync(route, first)).StatusCode);

        var conflict = await Admin.PutAsJsonAsync(route, second);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        using var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal(ProblemTypes.VersionConflict, body.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task UnknownDevice_Returns404()
    {
        var response = await Technician.GetAsync(ApiRoutes.ById(ApiRoutes.Equipment, Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // فهرست و جست‌وجو

    [Fact]
    public async Task Search_ArabicSpellingFindsPersianText()
    {
        var input = CaseInput();
        input.Brand = "کیس test"; // Persian kaf and yeh
        await CreateAsync(input);

        var result = await ListAsync("q=" + Uri.EscapeDataString("كيس"));

        Assert.Equal(1, result.Total);
    }

    [Theory]
    [InlineData("۵۵۶۶")] // Persian digits
    [InlineData("5566")]
    [InlineData("sn-77")]
    [InlineData("TEST-pc")]
    [InlineData("testbrand mymodel")] // every word must match, in any field
    public async Task Search_FindsByAssetSerialComputerNameAndWords(string query)
    {
        var input = CaseInput("5566");
        input.SerialNumber = "SN-77";
        input.ComputerName = "TEST-PC";
        input.Brand = "TestBrand";
        input.Model = "MyModel";
        await CreateAsync(input);
        await CreateAsync(MonitorInput("9999"));

        var result = await ListAsync("q=" + Uri.EscapeDataString(query));

        Assert.Equal("5566", Assert.Single(result.Items).AssetNumber);
    }

    [Fact]
    public async Task Filters_NarrowTheList()
    {
        var a = CaseInput("A1");
        var b = MonitorInput("A2");
        b.OwnerUnitId = UnitB.Id;
        b.FloorId = FloorB.Id;
        b.LocationUnitId = UnitB.Id;
        await CreateAsync(a);
        await CreateAsync(b);
        await CreateAsync(CaseInput(null));

        Assert.Equal(1, (await ListAsync("type=Monitor")).Total);
        Assert.Equal(1, (await ListAsync($"ownerUnitId={UnitB.Id}")).Total);
        Assert.Equal(1, (await ListAsync($"floorId={FloorB.Id}")).Total);
        Assert.Equal(1, (await ListAsync($"locationUnitId={UnitB.Id}")).Total);
        Assert.Equal(3, (await ListAsync($"buildingId={Building.Id}")).Total);
        Assert.Equal(1, (await ListAsync("noAssetNumber=true")).Total);
    }

    [Fact]
    public async Task Retired_HiddenByDefault_ShownOnRequest()
    {
        var device = await CreateAsync(CaseInput());
        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(device.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = device.Version });

        Assert.Equal(0, (await ListAsync()).Total);
        Assert.Equal(1, (await ListAsync("includeRetired=true")).Total);
        Assert.Equal(1, (await ListAsync("status=Retired")).Total);
    }

    [Fact]
    public async Task Paging_ReturnsTheRequestedPage_SortedWithBlanksLast()
    {
        await CreateAsync(CaseInput(null));
        await CreateAsync(CaseInput("B2"));
        await CreateAsync(CaseInput("A1"));

        var first = await ListAsync("pageSize=2");
        var second = await ListAsync("pageSize=2&page=2");

        Assert.Equal(3, first.Total);
        Assert.Equal(["A1", "B2"], first.Items.Select(i => i.AssetNumber));
        Assert.StartsWith("TEMP-", Assert.Single(second.Items).AssetNumber);
    }

    [Fact]
    public async Task Sort_ByEachColumn_BothDirections_BlanksLast()
    {
        var a = CaseInput("A1");
        a.Brand = "Zeta";
        a.ComputerName = "PC-B";
        a.OwnerUnitId = UnitB.Id;
        await CreateAsync(a);
        await Task.Delay(20);

        var b = CaseInput("A2");
        b.Brand = "Alpha";
        b.ComputerName = "PC-A";
        var second = await CreateAsync(b);
        await Task.Delay(20);

        var c = MonitorInput("A3");
        c.LocationUnitId = UnitB.Id;
        await CreateAsync(c);

        async Task<string[]> OrderAsync(string query) =>
            (await ListAsync(query)).Items.Select(i => i.AssetNumber!).ToArray();

        Assert.Equal(["A1", "A2", "A3"], await OrderAsync("sort=asset"));
        Assert.Equal(["A3", "A2", "A1"], await OrderAsync("sort=asset&desc=true"));

        // بدون نام کامپیوتر در هر دو جهت آخر است.
        Assert.Equal(["A2", "A1", "A3"], await OrderAsync("sort=computer"));
        Assert.Equal(["A1", "A2", "A3"], await OrderAsync("sort=computer&desc=true"));

        Assert.Equal(["A2", "A3", "A1"], await OrderAsync("sort=owner"));
        Assert.Equal(["A3", "A2", "A1"], await OrderAsync("sort=updated&desc=true"));

        // تغییر، دستگاه را جلو می‌آورد.
        var update = ToUpdate(second);
        update.Notes = "changed";
        (await Technician.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, second.Id), update)).EnsureSuccessStatusCode();
        Assert.Equal("A2", (await OrderAsync("sort=updated&desc=true"))[0]);

        // نام نامعتبر ترتیب پیش‌فرض است، نه خطا.
        Assert.Equal(["A1", "A2", "A3"], await OrderAsync("sort=nope"));
    }

    [Fact]
    public async Task Sort_AppliesAcrossPages()
    {
        foreach (var number in new[] { "A1", "A2", "A3" }) await CreateAsync(CaseInput(number));

        var firstPage = await ListAsync("sort=asset&desc=true&pageSize=2");
        var secondPage = await ListAsync("sort=asset&desc=true&pageSize=2&page=2");

        Assert.Equal(["A3", "A2"], firstPage.Items.Select(i => i.AssetNumber));
        Assert.Equal("A1", Assert.Single(secondPage.Items).AssetNumber);
    }

    // ثبت چندتایی

    private static BulkCreateRequest Bulk(params EquipmentInput[] items) => new() { Items = [.. items] };

    [Fact]
    public async Task Bulk_CreatesAll_WithHistory_InOneChangeSet()
    {
        var first = CaseInput("B1");
        first.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "8GB" });
        var second = CaseInput(null);
        second.Components.Add(new ComponentInput { Kind = ComponentKind.Ram, Capacity = "8GB" });

        var result = await PostOkAsync<BulkCreateResponse>(Technician, ApiRoutes.EquipmentBulk, Bulk(first, second));
        Assert.Equal(2, result.Created);

        var items = (await ListAsync()).Items;
        Assert.Equal(["B1", "TEMP-000001"], items.Select(i => i.AssetNumber));

        var firstRows = await HistoryAsync(Technician, items[0].Id);
        var secondRows = await HistoryAsync(Technician, items[1].Id);
        Assert.Equal(firstRows[0].ChangeSetId, secondRows[0].ChangeSetId);
        Assert.Equal("tech-a", firstRows[0].UserName);
        Assert.Equal("ثبت چندتایی", firstRows[0].Reason);
        Assert.Single((await GetAsync(Technician, items[1].Id)).Components);
    }

    [Fact]
    public async Task Bulk_OneBadRow_SavesNothing_AndNamesTheRow()
    {
        await CreateAsync(CaseInput("USED"));

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulk, Bulk(CaseInput("B1"), CaseInput("USED"), CaseInput("B1")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("1:AssetNumber", out _));
        Assert.True(errors.TryGetProperty("2:AssetNumber", out _));
        Assert.False(errors.TryGetProperty("0:AssetNumber", out _));

        Assert.Equal(1, (await ListAsync()).Total);
    }

    [Fact]
    public async Task Bulk_DuplicateAfterNormalizing_IsCaught()
    {
        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulk,
            Bulk(CaseInput("\u06F1\u06F2\u06F3"), CaseInput(" 123 ")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await ListAsync()).Total);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(201)]
    public async Task Bulk_CountOutOfRange_Returns400(int count)
    {
        var items = Enumerable.Range(0, count).Select(i => CaseInput($"N{i}")).ToArray();

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentBulk, Bulk(items));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_CanLinkAllToOneHub()
    {
        var hub = await CreateAsync(CaseInput("H1"));
        var a = MonitorInput("M1");
        a.LinkParentId = hub.Id;
        var b = MonitorInput("M2");
        b.LinkParentId = hub.Id;

        await PostOkAsync<BulkCreateResponse>(Technician, ApiRoutes.EquipmentBulk, Bulk(a, b));

        Assert.Equal(2, (await GetAsync(Technician, hub.Id)).Children.Count);
    }

    [Fact]
    public async Task Bulk_WithoutPermission_Returns403()
    {
        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.TechnicianPassword);

        var response = await client.PostAsJsonAsync(ApiRoutes.EquipmentBulk, Bulk(CaseInput("B1"), CaseInput("B2")));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Summary_CountsByStatus()
    {
        var device = await CreateAsync(CaseInput("A1"));
        await CreateAsync(MonitorInput("A2"));
        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(device.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.InRepair, Version = device.Version });

        var stats = await Technician.GetFromJsonAsync<EquipmentStats>(ApiRoutes.EquipmentSummary);

        Assert.Equal(2, stats!.Total);
        Assert.Equal(1, stats.Active);
        Assert.Equal(1, stats.InRepair);
    }

    [Fact]
    public async Task Lookup_FindsByAssetNumberAndComputerName()
    {
        var input = CaseInput("A1");
        input.ComputerName = "TEST-PC";
        await CreateAsync(input);

        var byAsset = await Technician.GetFromJsonAsync<List<EquipmentListItem>>($"{ApiRoutes.EquipmentLookup}?assetNumber=a1");
        var byName = await Technician.GetFromJsonAsync<List<EquipmentListItem>>($"{ApiRoutes.EquipmentLookup}?computerName=test-pc");
        var none = await Technician.GetFromJsonAsync<List<EquipmentListItem>>($"{ApiRoutes.EquipmentLookup}?assetNumber=zzz");

        Assert.Single(byAsset!);
        Assert.Single(byName!);
        Assert.Empty(none!);
    }

    // جابه‌جایی

    private MoveEquipmentRequest MoveTo(int version, bool moveLinked = true) => new()
    {
        BuildingId = Building.Id,
        FloorId = FloorB.Id,
        LocationUnitId = UnitB.Id,
        Room = "room-2",
        MoveLinked = moveLinked,
        Reason = "test move",
        Version = version
    };

    private async Task<(EquipmentDetail Hub, EquipmentDetail Child)> LinkedPairAsync()
    {
        var hub = await CreateAsync(CaseInput());
        var child = await CreateAsync(MonitorInput());
        hub = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentLink(hub.Id),
            new LinkRequest { ChildId = child.Id, Version = hub.Version });
        return (hub, await GetAsync(Technician, child.Id));
    }

    [Fact]
    public async Task Move_WithChildren_MovesAll_InOneChangeSet()
    {
        var (hub, child) = await LinkedPairAsync();

        var moved = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentMove(hub.Id), MoveTo(hub.Version));
        Assert.Equal(FloorB.Id, moved.FloorId);

        var movedChild = await GetAsync(Technician, child.Id);
        Assert.Equal(FloorB.Id, movedChild.FloorId);
        Assert.Equal("room-2", movedChild.Room);

        var hubRow = (await HistoryAsync(Technician, hub.Id)).First(r => r.Field == "FloorId");
        var childRow = (await HistoryAsync(Technician, child.Id)).First(r => r.Field == "FloorId");
        Assert.Equal(hubRow.ChangeSetId, childRow.ChangeSetId);
        Assert.Equal("test move", hubRow.Reason);
        Assert.Equal("tech-a", childRow.UserName);
    }

    [Fact]
    public async Task Move_WithoutChildren_LeavesThemInPlace()
    {
        var (hub, child) = await LinkedPairAsync();

        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentMove(hub.Id), MoveTo(hub.Version, moveLinked: false));

        Assert.Equal(FloorA.Id, (await GetAsync(Technician, child.Id)).FloorId);
    }

    [Fact]
    public async Task Move_RoomBecomesSearchable()
    {
        var device = await CreateAsync(CaseInput());

        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentMove(device.Id), MoveTo(device.Version));

        Assert.Equal(1, (await ListAsync("q=room-2")).Total);
    }

    [Fact]
    public async Task Move_StaleVersion_Returns409()
    {
        var device = await CreateAsync(CaseInput());

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentMove(device.Id), MoveTo(device.Version + 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Move_RetiredDevice_Returns400()
    {
        var device = await CreateAsync(CaseInput());
        var retired = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(device.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = device.Version });

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentMove(device.Id), MoveTo(retired.Version));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // وضعیت

    [Fact]
    public async Task Retire_WithoutReason_Returns400()
    {
        var device = await CreateAsync(CaseInput());

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentStatus(device.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "  ", Version = device.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RetiringAHub_DetachesChildren_WithHistory()
    {
        var (hub, child) = await LinkedPairAsync();

        var retired = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(hub.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "broken", Version = hub.Version });

        Assert.Empty(retired.Children);
        Assert.Null((await GetAsync(Technician, child.Id)).Parent);

        var history = await HistoryAsync(Technician, child.Id);
        var row = Assert.Single(history, r => r.Field == "ParentId" && r.NewValue is null);
        Assert.Equal("broken", row.Reason);
    }

    [Fact]
    public async Task RetiringAChild_DetachesIt()
    {
        var (hub, child) = await LinkedPairAsync();

        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(child.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "broken", Version = child.Version });

        Assert.Empty((await GetAsync(Technician, hub.Id)).Children);
    }

    [Fact]
    public async Task RestoringFromRetired_OnlyAdmin()
    {
        var device = await CreateAsync(CaseInput());
        var retired = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(device.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = device.Version });

        var restore = new ChangeStatusRequest { Status = EquipmentStatus.Active, Version = retired.Version };

        var technician = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentStatus(device.Id), restore);
        Assert.Equal(HttpStatusCode.Forbidden, technician.StatusCode);

        var admin = await Admin.PostAsJsonAsync(ApiRoutes.EquipmentStatus(device.Id), restore);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
    }

    // اتصال

    [Fact]
    public async Task LinkAndUnlink_WriteHistoryOnTheChild()
    {
        var (hub, child) = await LinkedPairAsync();
        Assert.Equal(hub.Id, child.Parent!.Id);
        Assert.Single(hub.Children);

        var after = await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentUnlink(hub.Id),
            new LinkRequest { ChildId = child.Id, Version = hub.Version });
        Assert.Empty(after.Children);

        var rows = (await HistoryAsync(Technician, child.Id)).Where(r => r.Field == "ParentId").ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => AuditLabels.Value(r.Field, r.NewValue) == "TEST-0001");
    }

    [Fact]
    public async Task Link_HubMustBeCaseOrLaptop()
    {
        var monitor = await CreateAsync(MonitorInput("A1"));
        var other = await CreateAsync(MonitorInput("A2"));

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentLink(monitor.Id),
            new LinkRequest { ChildId = other.Id, Version = monitor.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_ChildCannotBeACase()
    {
        var hub = await CreateAsync(CaseInput("A1"));
        var otherCase = await CreateAsync(CaseInput("A2"));

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id),
            new LinkRequest { ChildId = otherCase.Id, Version = hub.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_ChildAlreadyOnAnotherHub_Returns400()
    {
        var (_, child) = await LinkedPairAsync();
        var hub2 = await CreateAsync(CaseInput("A9"));

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub2.Id),
            new LinkRequest { ChildId = child.Id, Version = hub2.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_RetiredDevice_Returns400()
    {
        var hub = await CreateAsync(CaseInput());
        var child = await CreateAsync(MonitorInput());
        await PostOkAsync<EquipmentDetail>(Technician, ApiRoutes.EquipmentStatus(child.Id),
            new ChangeStatusRequest { Status = EquipmentStatus.Retired, Reason = "test", Version = child.Version });

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id),
            new LinkRequest { ChildId = child.Id, Version = hub.Version });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_StaleHubVersion_Returns409()
    {
        var hub = await CreateAsync(CaseInput());
        var child = await CreateAsync(MonitorInput());

        var response = await Technician.PostAsJsonAsync(ApiRoutes.EquipmentLink(hub.Id),
            new LinkRequest { ChildId = child.Id, Version = hub.Version + 5 });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }
}
