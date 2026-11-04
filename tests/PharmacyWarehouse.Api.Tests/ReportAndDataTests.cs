using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using PharmacyWarehouse.Api.DataTools;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.Tests;

public class ReportAndDataTests : EquipmentTestBase
{
    private static readonly string[] ImportHeader =
        ["نوع", "شماره اموال", "برند", "مدل", "سایز", "واحد صاحب اموال", "ساختمان", "طبقه", "واحد محل"];

    private string?[] MonitorRow(string? asset, string size = "22") =>
        ["مانیتور", asset, "TestBrand", "TestModel", size, "test-unit-a", "test-building", "floor-a", "test-unit-a"];

    private static byte[] Workbook(params string?[][] rows) =>
        XlsxWriter.Write(new XlsxSheet("sheet", ImportHeader, rows));

    private static async Task<HttpResponseMessage> PostFileAsync(HttpClient client, string route, byte[] file, bool createMissing = false)
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(part, "file", "list.xlsx");
        var separator = route.Contains('?') ? '&' : '?';
        return await client.PostAsync($"{route}{separator}createMissing={(createMissing ? "true" : "false")}", content);
    }

    private static async Task<ImportResult> ImportOkAsync(HttpClient client, string route, byte[] file, bool createMissing = false)
    {
        var response = await PostFileAsync(client, route, file, createMissing);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportResult>())!;
    }

    // Xlsx

    [Fact]
    public void Xlsx_RoundTrip_KeepsTextLeadingZerosAndPositions()
    {
        var bytes = XlsxWriter.Write(new XlsxSheet("برگه", ["الف", "ب", "ج"],
        [
            ["00123", null, "=1+1"],
            [null, "کیس <&> \"x\"", null]
        ]));

        var rows = XlsxReader.ReadFirstSheet(new MemoryStream(bytes));

        Assert.Equal("الف", rows[0][0]);
        Assert.Equal("00123", rows[1][0]);
        Assert.Null(rows[1][1]);
        Assert.Equal("=1+1", rows[1][2]);
        Assert.Equal("کیس <&> \"x\"", rows[2][1]);
    }

    [Fact]
    public void Xlsx_HasThePartsExcelNeeds_AndEveryPartIsWellFormed()
    {
        using var zip = new ZipArchive(new MemoryStream(XlsxWriter.Write(new XlsxSheet("[بد/نام]", ["a"], [["1"]]))));

        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
        {
            var entry = zip.GetEntry(part);
            Assert.NotNull(entry);
            using var stream = entry.Open();
            System.Xml.Linq.XDocument.Load(stream);
        }

        using var workbook = zip.GetEntry("xl/workbook.xml")!.Open();
        Assert.DoesNotContain("[", new StreamReader(workbook).ReadToEnd());
    }

    [Fact]
    public void Xlsx_ReadsSharedStrings_AsExcelWritesThem()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }

            const string main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Add("xl/workbook.xml", $"<workbook xmlns=\"{main}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"s\" sheetId=\"1\" r:id=\"rId7\"/></sheets></workbook>");
            Add("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId7\" Type=\"x\" Target=\"worksheets/data.xml\"/></Relationships>");
            Add("xl/sharedStrings.xml", $"<sst xmlns=\"{main}\"><si><t>hello</t></si><si><r><t>wo</t></r><r><t>rld</t></r></si></sst>");
            Add("xl/worksheets/data.xml", $"<worksheet xmlns=\"{main}\"><sheetData><row r=\"2\"><c r=\"B2\" t=\"s\"><v>1</v></c><c r=\"C2\"><v>42</v></c></row></sheetData></worksheet>");
        }

        buffer.Position = 0;
        var rows = XlsxReader.ReadFirstSheet(buffer);

        Assert.Empty(rows[0]);
        Assert.Equal("world", rows[1][1]);
        Assert.Equal("42", rows[1][2]);
    }

    // Reports

    [Fact]
    public async Task EveryReport_ReturnsATable_AndAnExcelFile()
    {
        await CreateAsync(CaseInput("A1"));

        foreach (var report in ReportCatalog.All)
        {
            var table = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(report.Key, ""));
            Assert.NotEmpty(table!.Columns);

            var excel = await Technician.GetAsync(ApiRoutes.ReportExcel(report.Key, ""));
            Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
            Assert.Equal(DataEndpoints.XlsxContentType, excel.Content.Headers.ContentType?.MediaType);

            var rows = XlsxReader.ReadFirstSheet(await excel.Content.ReadAsStreamAsync());
            Assert.Equal(table.Columns, rows[0]);
        }
    }

    [Fact]
    public async Task UnknownReport_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Technician.GetAsync(ApiRoutes.Report("nope", ""))).StatusCode);
    }

    [Fact]
    public async Task EquipmentReport_Filters()
    {
        await CreateAsync(CaseInput("A1"));
        var other = MonitorInput("A2");
        other.OwnerUnitId = UnitB.Id;
        await CreateAsync(other);

        var byOwner = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.Equipment, $"ownerUnitId={UnitB.Id}"));
        var byType = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.Equipment, "type=Case"));

        Assert.Single(byOwner!.Rows);
        Assert.Single(byType!.Rows);
    }

    [Fact]
    public async Task ByOwnerReport_Counts()
    {
        await CreateAsync(CaseInput("A1"));
        await CreateAsync(CaseInput("A2"));

        var table = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.ByOwner, ""));

        Assert.True(new string?[] { "test-unit-a", "کیس", "2" }.SequenceEqual(Assert.Single(table!.Rows)));
    }

    [Fact]
    public async Task RecentChanges_ShowsWhoDidWhat_EvenAfterDelete()
    {
        var device = await CreateAsync(CaseInput("A1"));
        var update = ToUpdate(device);
        update.Brand = "NewBrand";
        (await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), update)).EnsureSuccessStatusCode();
        (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id))).EnsureSuccessStatusCode();

        var table = await Technician.GetFromJsonAsync<ReportTable>(ApiRoutes.Report(ReportCatalog.RecentChanges, "days=7"));

        var change = Assert.Single(table!.Rows, r => r[4] == "برند");
        Assert.Equal("admin-a", change[1]);
        Assert.Equal("دستگاه A1", change[2]);
        Assert.Equal("NewBrand", change[6]);

        Assert.Contains(table.Rows, r => r[3] == "حذف" && r[2] == "دستگاه A1");
    }

    [Fact]
    public async Task ChangeSets_GroupOneSaveIntoOneRow_WithDetails()
    {
        var input = CaseInput("CS1");
        input.Components =
        [
            new PharmacyWarehouse.Domain.Assets.ComponentInput { Kind = PharmacyWarehouse.Domain.Assets.ComponentKind.Cpu, Brand = "B", Model = "M" },
            new PharmacyWarehouse.Domain.Assets.ComponentInput { Kind = PharmacyWarehouse.Domain.Assets.ComponentKind.Ram, Brand = "B", Model = "M", Capacity = "8GB" }
        ];
        var device = await CreateAsync(input);

        var update = ToUpdate(device);
        update.Brand = "NewBrand";
        (await Admin.PutAsJsonAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id), update)).EnsureSuccessStatusCode();

        var sets = (await Technician.GetFromJsonAsync<List<ChangeSetItem>>(ApiRoutes.RecentChangeSets + "?days=7"))!;

        var created = Assert.Single(sets, s => s.Summary.StartsWith("ثبت کیس"));
        Assert.Equal("ثبت کیس CS1 با ۲ قطعه", created.Summary);
        Assert.Equal(3, created.Lines.Count);
        Assert.Equal(device.Id, created.EquipmentId);
        Assert.Equal("tech-a", created.UserName);

        var edited = Assert.Single(sets, s => s.Summary.StartsWith("ویرایش"));
        Assert.Contains(edited.Lines, l => l.Field == "برند" && l.To == "NewBrand");

        // جدیدترین اول.
        Assert.True(sets.IndexOf(edited) < sets.IndexOf(created));
    }

    [Fact]
    public async Task ChangeSets_NameDeletedDevices_AndNeedReportPermission()
    {
        var device = await CreateAsync(MonitorInput("DEL1"));
        (await Admin.DeleteAsync(ApiRoutes.ById(ApiRoutes.Equipment, device.Id))).EnsureSuccessStatusCode();

        var sets = (await Technician.GetFromJsonAsync<List<ChangeSetItem>>(ApiRoutes.RecentChangeSets))!;
        var deleted = Assert.Single(sets, s => s.Summary.StartsWith("حذف"));
        Assert.Equal("حذف مانیتور DEL1", deleted.Summary);
        Assert.Null(deleted.EquipmentId);

        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.TechnicianPassword);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.RecentChangeSets)).StatusCode);
    }

    [Fact]
    public async Task ReportWithoutPermission_Returns403()
    {
        await App.AddUserWithoutRoleAsync("no-role");
        using var client = await LoggedInAsync("no-role", TestApp.TechnicianPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(ApiRoutes.Report(ReportCatalog.Equipment, ""))).StatusCode);
    }

    // Import

    [Fact]
    public async Task Technician_CannotImportOrDownloadBackupByDefault()
    {
        var file = Workbook(MonitorRow("I1"));

        Assert.Equal(HttpStatusCode.Forbidden, (await PostFileAsync(Technician, ApiRoutes.DataImportCommit, file)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Technician.GetAsync(ApiRoutes.DataBackup)).StatusCode);
    }

    [Fact]
    public async Task Preview_SavesNothing_Commit_SavesValidRows_WithHistory()
    {
        await CreateAsync(MonitorInput("USED"));
        var file = Workbook(MonitorRow("I1"), MonitorRow("USED"), MonitorRow("I1"), MonitorRow("I2", size: "5"), MonitorRow("I3"));

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, file);

        Assert.False(preview.Committed);
        Assert.Equal(5, preview.Total);
        Assert.Equal(2, preview.Valid);
        Assert.Equal([3, 4, 5], preview.Problems.Select(p => p.Row));

        var before = await Admin.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ApiRoutes.Equipment);
        Assert.Equal(1, before!.Total);

        var result = await ImportOkAsync(Admin, ApiRoutes.DataImportCommit, file);
        Assert.Equal(2, result.Imported);

        var after = await Admin.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ApiRoutes.Equipment + "?q=TestModel");
        Assert.Equal(3, after!.Total);

        var imported = after.Items.Single(i => i.AssetNumber == "I1");
        var row = Assert.Single(await HistoryAsync(Admin, imported.Id));
        Assert.Equal("admin-a", row.UserName);
        Assert.Equal("ورود از Excel", row.Reason);
    }

    [Fact]
    public async Task EmptyAssetNumber_GetsDistinctTemporaryNumbers()
    {
        await ImportOkAsync(Admin, ApiRoutes.DataImportCommit, Workbook(MonitorRow(null), MonitorRow(null)));

        var list = await Admin.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ApiRoutes.Equipment + "?noAssetNumber=true");

        Assert.Equal(["TEMP-000001", "TEMP-000002"], list!.Items.Select(i => i.AssetNumber));
    }

    [Fact]
    public async Task UnknownPlaces_AreErrors_UnlessAskedToCreate()
    {
        string?[] row = ["مانیتور", "I1", "B", "M", "22", "new-unit", "new-building", "new-floor", "new-unit"];

        var refused = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, Workbook(row));
        Assert.Equal(0, refused.Valid);

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, Workbook(row), createMissing: true);
        Assert.Equal(1, preview.Valid);
        Assert.Equal(3, preview.WillCreate.Length);

        // پیش‌نمایش چیزی نساخته است.
        var units = await Admin.GetFromJsonAsync<List<UnitItem>>(ApiRoutes.Units);
        Assert.DoesNotContain(units!, u => u.Name == "new-unit");

        var result = await ImportOkAsync(Admin, ApiRoutes.DataImportCommit, Workbook(row), createMissing: true);
        Assert.Equal(1, result.Imported);

        units = await Admin.GetFromJsonAsync<List<UnitItem>>(ApiRoutes.Units);
        Assert.Contains(units!, u => u.Name == "new-unit");
    }

    [Fact]
    public async Task ExportedList_CanBeImportedBack_ReportingDuplicates()
    {
        await CreateAsync(CaseInput("A1"));

        var export = await Technician.GetAsync(ApiRoutes.ReportExcel(ReportCatalog.Equipment, ""));
        var bytes = await export.Content.ReadAsByteArrayAsync();

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, bytes);

        Assert.Equal(1, preview.Total);
        Assert.Contains("تکراری", Assert.Single(preview.Problems).Errors[0]);
    }

    [Fact]
    public async Task Template_HasTheRequiredColumns_AndItsSampleIsAccepted()
    {
        var template = await Admin.GetAsync(ApiRoutes.DataTemplate);
        var bytes = await template.Content.ReadAsByteArrayAsync();

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, bytes, createMissing: true);

        Assert.Equal(1, preview.Valid);
    }

    [Fact]
    public async Task SingleType_NeedsNoTypeColumn_AndRejectsOtherTypes()
    {
        string[] header = ["شماره اموال", "برند", "مدل", "سایز", "واحد صاحب اموال", "ساختمان", "طبقه", "واحد محل"];
        string?[] monitor = ["T1", "B", "M", "22", "test-unit-a", "test-building", "floor-a", "test-unit-a"];
        var noType = XlsxWriter.Write(new XlsxSheet("s", header, [monitor]));

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview + "?types=Monitor", noType);
        Assert.Equal(1, preview.Valid);
        Assert.DoesNotContain("نوع", preview.Columns);

        // دو نوع بدون ستون «نوع»: معلوم نیست هر ردیف چیست.
        var both = await PostFileAsync(Admin, ApiRoutes.DataImportPreview + "?types=Case,Monitor", noType);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);

        // ستون «نوع» با نوعی که انتخاب نشده.
        var wrong = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview + "?types=Case", Workbook(MonitorRow("T2")));
        Assert.Equal(0, wrong.Valid);
        Assert.Contains("انتخاب نشده", Assert.Single(wrong.Problems).Errors[0]);

        Assert.Equal(HttpStatusCode.BadRequest, (await PostFileAsync(Admin, ApiRoutes.DataImportPreview + "?types=Toaster", noType)).StatusCode);
    }

    [Fact]
    public async Task RowsFixedInBrowser_AreCheckedAgain_ThenCommitted()
    {
        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview, Workbook(MonitorRow("F1"), MonitorRow("F2", size: "5")));
        Assert.Equal(1, preview.Valid);

        var bad = Assert.Single(preview.Rows, r => r.Errors.Length > 0);
        Assert.Equal(3, bad.Row);
        bad.Values["سایز"] = "24";

        var request = new ImportRowsRequest { Rows = preview.Rows };
        var check = await PostOkAsync<ImportResult>(Admin, ApiRoutes.DataImportCheck, request);
        Assert.Equal(2, check.Valid);
        Assert.Empty(check.Problems);

        var none = await Admin.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ApiRoutes.Equipment + "?q=TestModel");
        Assert.Equal(0, none!.Total);

        var commit = await PostOkAsync<ImportResult>(Admin, ApiRoutes.DataImportCommitRows, request);
        Assert.Equal(2, commit.Imported);

        var list = await Admin.GetFromJsonAsync<PagedResult<EquipmentListItem>>(ApiRoutes.Equipment + "?q=TestModel");
        Assert.Equal(["F1", "F2"], list!.Items.Select(i => i.AssetNumber).Order());

        Assert.Equal(HttpStatusCode.Forbidden, (await Technician.PostAsJsonAsync(ApiRoutes.DataImportCommitRows, request)).StatusCode);
    }

    [Fact]
    public async Task TypeTemplate_HasOnlyItsColumns_AndItsSampleIsAccepted()
    {
        var response = await Admin.GetAsync(ApiRoutes.DataTemplate + "?types=Case");
        var bytes = await response.Content.ReadAsByteArrayAsync();

        var header = XlsxReader.ReadFirstSheet(new MemoryStream(bytes))[0];
        Assert.DoesNotContain("نوع", header);
        Assert.DoesNotContain("سایز", header);
        Assert.Contains("نام کامپیوتر", header);

        var preview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview + "?types=Case", bytes, createMissing: true);
        Assert.Equal(1, preview.Valid);

        var both = await Admin.GetAsync(ApiRoutes.DataTemplate + "?types=Case,Monitor");
        var bothBytes = await both.Content.ReadAsByteArrayAsync();
        Assert.Contains("نوع", XlsxReader.ReadFirstSheet(new MemoryStream(bothBytes))[0]);
        var bothPreview = await ImportOkAsync(Admin, ApiRoutes.DataImportPreview + "?types=Case,Monitor", bothBytes, createMissing: true);
        Assert.Equal(2, bothPreview.Valid);
    }

    [Fact]
    public async Task BadFile_Returns400()
    {
        var notExcel = await PostFileAsync(Admin, ApiRoutes.DataImportPreview, "not a workbook"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, notExcel.StatusCode);

        var wrongColumns = XlsxWriter.Write(new XlsxSheet("s", ["x"], [["1"]]));
        var missing = await PostFileAsync(Admin, ApiRoutes.DataImportPreview, wrongColumns);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

        using var body = JsonDocument.Parse(await missing.Content.ReadAsStringAsync());
        Assert.Contains("نوع", body.RootElement.GetProperty("title").GetString());
    }

    // Backup

    [Fact]
    public async Task Backup_IsAReadableDatabase_WithTheData()
    {
        await CreateAsync(CaseInput("BACKUP-1"));

        var response = await Admin.GetAsync(ApiRoutes.DataBackup);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var path = Path.Combine(Path.GetTempPath(), $"emu-test-backup-{Guid.NewGuid():N}.db");
        await File.WriteAllBytesAsync(path, await response.Content.ReadAsByteArrayAsync());

        try
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Equipment WHERE AssetNumber = 'BACKUP-1'";
            Assert.Equal(1L, await command.ExecuteScalarAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ExportAll_HasEverySheetOnFirstRead()
    {
        await CreateAsync(CaseInput("A1"));

        var response = await Admin.GetAsync(ApiRoutes.DataExport);
        var rows = XlsxReader.ReadFirstSheet(await response.Content.ReadAsStreamAsync());

        Assert.Equal(2, rows.Count);

        using var zip = new ZipArchive(await (await Admin.GetAsync(ApiRoutes.DataExport)).Content.ReadAsStreamAsync());
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet5.xml"));
    }
}
