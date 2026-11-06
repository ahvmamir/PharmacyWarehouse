using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.DataTools;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Import;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Tests;

public class ImportTests : ApiTestBase
{
    private HttpClient Keeper { get; set; } = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await App.AddKeeperAsync("keeper-a");
        await App.AddStaffAsync("staff-a");
        Keeper = await LoggedInAsync("keeper-a", TestApp.StaffPassword);
    }

    private static readonly string[] Header = ["نام دارو و دوز", "نام تجاری", "تعداد", "تعداد در بسته", "بچ نامبر", "تاریخ انقضا", "بازرسی دوره‌ای"];

    // ساختار فایل واقعی: ردیف ۱ «تاریخ بروزرسانی»، ردیف ۲ سرستون، بعد داده و ردیف‌های قالب فقط «*».
    // XlsxWriter ردیف ۱ را از Columns می‌سازد، پس توضیح در Columns و سرستون واقعی اولین ردیف داده است.
    private static XlsxSheet Sheet(string name, params string?[][] data)
    {
        var rows = new List<string?[]> { Header.Cast<string?>().ToArray() };
        rows.AddRange(data);
        rows.Add(["", "", "", "", "*", "*"]);
        return new XlsxSheet(name, ["تاریخ بروزرسانی: آزمایشی"], rows);
    }

    private static byte[] Workbook(params XlsxSheet[] sheets) => XlsxWriter.Write(sheets);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, string route = ApiRoutes.ImportParse)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "test.xlsx");
        return await client.PostAsync(route, content);
    }

    private static async Task<ImportPreview> ParseOkAsync(HttpClient client, byte[] bytes)
    {
        var response = await UploadAsync(client, bytes);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportPreview>())!;
    }

    private static Task<HttpResponseMessage> CommitAsync(HttpClient client, ImportPreview preview) =>
        client.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = preview.Rows.ToList() });

    // ردیف: نام، برند، تعداد، تعداد در بسته، بچ، انقضا
    private static string?[] R(string name, string qty, string batch, string expiry, string? brand = null, string pack = "30") =>
        [name, brand, qty, pack, batch, expiry];

    private static ImportRowInput Input(string sheet, string batch, string name = "داروی آزمایشی ادغام") =>
        new() { Sheet = sheet, RowNumber = 2, Name = name, Quantity = "2", BatchNumber = batch, Expiry = "31/05/2028" };

    private Task<HttpResponseMessage> CommitRowsAsync(params ImportRowInput[] rows) =>
        Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows.ToList() });

    [Fact]
    public async Task Commit_InactiveProduct_IsRejected_AndNothingIsPosted()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی غیرفعال", null);
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            (await db.Products.FirstAsync(p => p.Id == product)).IsActive = false;
            await db.SaveChangesAsync();
        }

        var response = await CommitRowsAsync(Input("ضلع آزمایشی", "TEST-IN1", "داروی آزمایشی غیرفعال"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var check = App.CreateScope();
        Assert.Empty(await check.ServiceProvider.GetRequiredService<WarehouseDbContext>().Batches.Where(b => b.ProductId == product).ToListAsync());
    }

    [Fact]
    public async Task Commit_RowInAnotherSheet_KeepsItsLocationOnTheBatch()
    {
        var response = await CommitRowsAsync(Input("ضلع یک", "TEST-L1"), Input("ضلع دو", "TEST-L2"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var one = await db.StorageLocations.AsNoTracking().FirstAsync(l => l.Name == "ضلع یک");
        var two = await db.StorageLocations.AsNoTracking().FirstAsync(l => l.Name == "ضلع دو");
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Name == "داروی آزمایشی ادغام");
        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == product.Id).ToListAsync();

        Assert.Equal(one.Id, product.LocationId);
        Assert.Null(batches.Single(b => b.BatchNumber == "TEST-L1").LocationId); // همان محل قلم
        Assert.Equal(two.Id, batches.Single(b => b.BatchNumber == "TEST-L2").LocationId);
    }

    [Fact]
    public async Task Commit_ExistingProduct_BatchGetsTheRowLocation_ProductUnchanged()
    {
        var product = await App.AddProductRawAsync("داروی آزمایشی موجود", null);

        var response = await CommitRowsAsync(Input("ضلع سه", "TEST-E1", "داروی آزمایشی موجود"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var location = await db.StorageLocations.AsNoTracking().FirstAsync(l => l.Name == "ضلع سه");
        Assert.Null((await db.Products.AsNoTracking().FirstAsync(p => p.Id == product)).LocationId);
        Assert.Equal(location.Id, (await db.Batches.AsNoTracking().FirstAsync(b => b.ProductId == product)).LocationId);
    }

    [Fact]
    public async Task Commit_NewProduct_BarcodeAndMinStockFromLaterRows_AreApplied_RepeatedBarcodeOnce()
    {
        var first = Input("ضلع یک", "TEST-M1");
        var second = Input("ضلع یک", "TEST-M2");
        second.Barcode = "TEST-0777";
        second.MinStock = "6";
        var third = Input("ضلع یک", "TEST-M3");
        third.Barcode = "TEST-0777";

        var response = await CommitRowsAsync(first, second, third);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Name == "داروی آزمایشی ادغام");
        Assert.Equal(6, product.MinStock);
        Assert.Contains("0777", product.SearchText);
        Assert.Equal("TEST-0777", (await db.ProductBarcodes.AsNoTracking().SingleAsync(c => c.ProductId == product.Id)).Code);
    }

    [Theory]
    [InlineData(ApiRoutes.ImportValidate)]
    [InlineData(ApiRoutes.ImportCommit)]
    public async Task NullRowElement_Returns400_Not500(string route)
    {
        var response = await Keeper.PostAsync(route, new StringContent("""{"rows":[null]}""", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parse_ImpossibleRowIndex_Returns400()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open());
                w.Write(xml);
            }

            Add("xl/workbook.xml", """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="S" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add("xl/_rels/workbook.xml.rels", """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add("xl/worksheets/sheet1.xml", """<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row r="2147483647"/></sheetData></worksheet>""");
        }

        var response = await UploadAsync(Keeper, ms.ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Parse_ReadsAllSheets_SkipsTemplateRows_KeepsExcelRowNumbers_IgnoresUnknownColumns()
    {
        var bytes = Workbook(
            Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028"), R("داروی آزمایشی ۲", "3", "TEST-B2", "07/2029")),
            Sheet("اعصاب آزمایشی", R("داروی آزمایشی ۳", "0", "*", "*")));

        var preview = await ParseOkAsync(Keeper, bytes);

        Assert.Equal(3, preview.Rows.Count);
        Assert.Equal(["ضلع آزمایشی", "ضلع آزمایشی", "اعصاب آزمایشی"], preview.Rows.Select(r => r.Sheet));
        Assert.Equal(3, preview.Rows[0].RowNumber); // ردیف ۱ توضیح، ردیف ۲ سرستون، داده از ردیف ۳
        Assert.True(preview.CanCommit);
        Assert.Equal(3, preview.Summary.NewProducts);
        Assert.Equal(2, preview.Summary.NewBatches);
        Assert.Equal(1, preview.Summary.ZeroQuantityProducts);
        Assert.Equal(["ضلع آزمایشی", "اعصاب آزمایشی"], preview.Summary.NewLocations);
        Assert.True(preview.IgnoredRows >= 0);
    }

    [Fact]
    public async Task Parse_Garbage_Empty_AndNoHeader_Return400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(Keeper, [1, 2, 3])).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(Keeper, [])).StatusCode);

        var noHeader = Workbook(new XlsxSheet("بی‌سرستون", ["الف", "ب"], [["1", "2"]]));
        var response = await UploadAsync(Keeper, noHeader);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("نام دارو", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Parse_ReportsErrorsPerRow_AndBlocksCommit()
    {
        var bytes = Workbook(Sheet("ضلع آزمایشی", R("داروی خوب", "5", "TEST-B1", "31/05/2028"), R("داروی بد", "186+C97", "TEST-B2", "13/062028")));

        var preview = await ParseOkAsync(Keeper, bytes);

        Assert.False(preview.CanCommit);
        Assert.Equal(ImportSeverity.Ok, preview.Statuses[0].Severity);
        Assert.Equal(ImportSeverity.Error, preview.Statuses[1].Severity);
        Assert.Equal(2, preview.Statuses[1].Messages.Count); // تعداد و تاریخ
    }

    [Fact]
    public async Task Commit_CreatesLocationsProductsBatchesAndImportMovements_ConsistentLedger_AuditReason()
    {
        var bytes = Workbook(
            Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028", brand: "برند"), R("داروی آزمایشی ۱", "2", "TEST-B2", "07/2029", brand: "برند"),
                R("داروی صفر", "0", "TEST-ZZ", "13/45/2028")),
            Sheet("اعصاب آزمایشی", R("داروی آزمایشی ۴", "7", "TEST-B4", "01/02/1408")));
        var preview = await ParseOkAsync(Keeper, bytes);
        Assert.True(preview.CanCommit);

        var response = await CommitAsync(Keeper, preview);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(new ImportResult(3, 0, 3, 2, 0), result);

        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var batches = await db.Batches.AsNoTracking().OrderBy(b => b.BatchNumber).ToListAsync();
        Assert.Equal(["TEST-B1", "TEST-B2", "TEST-B4"], batches.Select(b => b.BatchNumber));
        Assert.Equal([5, 2, 7], batches.Select(b => b.OnHand));
        Assert.Equal(ExpiryPrecision.Month, batches[1].ExpiryPrecision);
        Assert.Equal(CalendarKind.Jalali, batches[2].ExpiryCalendar);
        Assert.All(await db.StockMovements.AsNoTracking().ToListAsync(), m => Assert.Equal(StockMovementKind.Import, m.Kind));
        await App.AssertLedgerConsistentAsync();

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        Assert.Equal(["اعصاب آزمایشی", "ضلع آزمایشی"], locations.Select(l => l.Name).OrderBy(n => n, StringComparer.Ordinal));
        var zero = await db.Products.AsNoTracking().FirstAsync(p => p.NameKey == "داروی صفر".Replace(" ", "").ToUpperInvariant());
        Assert.False(await db.Batches.AnyAsync(b => b.ProductId == zero.Id));
        Assert.NotNull(zero.LocationId);

        // FEFO بین بچ‌های یک قلم: ترتیب ثبت به ترتیب ردیف جلو می‌رود.
        Assert.True(batches[0].CreatedAtUtc < batches[1].CreatedAtUtc);
        var audit = await db.AuditEntries.AsNoTracking().Where(a => a.EntityType == "Product" && a.Action == "Created").ToListAsync();
        Assert.Equal(3, audit.Count);
        Assert.All(audit, a => Assert.Equal("ورود از Excel", a.Reason));
    }

    [Fact]
    public async Task Commit_WithAnyError_Returns409_AndCreatesNothing()
    {
        var bytes = Workbook(Sheet("ضلع آزمایشی", R("داروی خوب", "5", "TEST-B1", "31/05/2028"), R("داروی بد", "x", "TEST-B2", "31/05/2028")));
        var preview = await ParseOkAsync(Keeper, bytes);

        var response = await CommitAsync(Keeper, preview);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(0, await db.Products.CountAsync());
        Assert.Equal(0, await db.StorageLocations.CountAsync());
        Assert.Equal(0, await db.AuditEntries.CountAsync(a => a.EntityType == "Product"));
    }

    [Fact]
    public async Task Commit_Twice_SecondIsRejectedAsDuplicateBatch_StockNotDoubled()
    {
        var bytes = Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028")));
        var preview = await ParseOkAsync(Keeper, bytes);
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(Keeper, preview)).StatusCode);

        var again = await ParseOkAsync(Keeper, bytes);

        Assert.False(again.CanCommit);
        Assert.Contains(again.Statuses[0].Messages, m => m.Contains("قبلاً در سیستم"));
        Assert.Equal(HttpStatusCode.Conflict, (await CommitAsync(Keeper, again)).StatusCode);
        await App.AssertLedgerConsistentAsync();
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(5, (await db.Batches.AsNoTracking().SingleAsync()).OnHand);
    }

    [Fact]
    public async Task Commit_NewBatchForExistingProduct_AddsBatchWithoutTouchingTheProduct()
    {
        var first = await ParseOkAsync(Keeper, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028"))));
        await CommitAsync(Keeper, first);
        Guid productId; int version;
        using (var scope = App.CreateScope())
        {
            var p = await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Products.AsNoTracking().SingleAsync();
            (productId, version) = (p.Id, p.Version);
        }

        var second = await ParseOkAsync(Keeper, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "9", "TEST-B2", "01/01/2031"))));
        Assert.Equal(1, second.Summary.ExistingProducts);
        var response = await CommitAsync(Keeper, second);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(0, result.ProductsCreated);
        Assert.Equal(1, result.ProductsExisting);
        using var verify = App.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(2, await db.Batches.CountAsync(b => b.ProductId == productId));
        Assert.Equal(version, (await db.Products.AsNoTracking().SingleAsync()).Version);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Validate_AfterFixingRows_BecomesCommittable_AndCommitUsesServerRevalidation()
    {
        var bad = await ParseOkAsync(Keeper, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "13/062028"))));
        Assert.False(bad.CanCommit);

        bad.Rows[0].Expiry = "13/06/2028";
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportValidate, new ImportRequest { Rows = bad.Rows.ToList() });
        var fixedPreview = (await response.Content.ReadFromJsonAsync<ImportPreview>())!;

        Assert.True(fixedPreview.CanCommit);
        Assert.Equal(HttpStatusCode.OK, (await CommitAsync(Keeper, fixedPreview)).StatusCode);
    }

    [Fact]
    public async Task Commit_TamperedRows_AreRevalidated_NotTrusted()
    {
        var preview = await ParseOkAsync(Keeper, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028"))));
        preview.Rows[0].Quantity = "-5";

        Assert.Equal(HttpStatusCode.Conflict, (await CommitAsync(Keeper, preview)).StatusCode);
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Commit_BarcodeAndMinStockAndPrice_AreStoredForNewProducts()
    {
        var rows = new List<ImportRowInput>
        {
            new() { Sheet = "ضلع آزمایشی", Name = "داروی آزمایشی ۱", Quantity = "4", BatchNumber = "TEST-B1", Expiry = "31/05/2030", Barcode = "TEST-0001", MinStock = "3", UnitCost = "1,500" }
        };

        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        var product = await db.Products.AsNoTracking().SingleAsync();
        Assert.Equal(3, product.MinStock);
        Assert.Contains("TEST-0001", product.SearchText);
        Assert.Equal("TEST-0001", (await db.ProductBarcodes.AsNoTracking().SingleAsync()).Code);
        Assert.Equal(1500, (await db.Batches.AsNoTracking().SingleAsync()).UnitCost);

        var found = await Keeper.GetAsync(ApiRoutes.ProductByCode("TEST-0001"));
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
    }

    [Fact]
    public async Task Endpoints_NeedImportPermission_StaffGets403()
    {
        var staff = await LoggedInAsync("staff-a", TestApp.StaffPassword);

        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(staff, [1])).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(ApiRoutes.ImportValidate, new ImportRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ApiRoutes.ImportTemplate)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(ApiRoutes.DataExport)).StatusCode);
    }

    [Fact]
    public async Task Upload_WithoutCsrfHeader_IsRejected()
    {
        var client = App.NewClient(csrfHeader: false);
        await TestApp.LoginAsync(client, "keeper-a", TestApp.StaffPassword);

        Assert.NotEqual(HttpStatusCode.OK, (await UploadAsync(client, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028"))))).StatusCode);
    }

    [Fact]
    public async Task Template_IsAReadableWorkbookWithTheStandardHeader_AndItsSampleRowIsImportable()
    {
        var response = await Keeper.GetAsync(ApiRoutes.ImportTemplate);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var sheets = XlsxReader.ReadAllSheets(new MemoryStream(bytes));
        Assert.Equal("اقلام", sheets[0].Name);
        Assert.Contains("نام دارو و دوز", sheets[0].Rows[0]);

        var preview = await ParseOkAsync(Keeper, bytes);
        Assert.True(preview.CanCommit);
        Assert.Single(preview.Rows);
    }

    [Fact]
    public async Task Export_RoundTripsIntoAFreshInstance_AsNewProducts()
    {
        var preview = await ParseOkAsync(Keeper, Workbook(
            Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028", brand: "برند"), R("داروی صفر", "0", "*", "*"))));
        await CommitAsync(Keeper, preview);

        var response = await Keeper.GetAsync(ApiRoutes.DataExport);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var sheet = XlsxReader.ReadAllSheets(new MemoryStream(bytes)).Single();
        Assert.Equal(3, sheet.Rows.Count); // سرستون + دو قلم
        Assert.Contains("قیمت", sheet.Rows[0]);

        // همان فایل در دیتابیس دیگر (تازه) دوباره قابل ورود است.
        await using var other = await TestApp.CreateAsync();
        await other.AddKeeperAsync("keeper-b");
        var client = other.NewClient();
        Assert.Equal(HttpStatusCode.OK, (await TestApp.LoginAsync(client, "keeper-b", TestApp.StaffPassword)).StatusCode);
        var reimport = await ParseOkAsync(client, bytes);
        Assert.True(reimport.CanCommit);
        Assert.Equal(2, reimport.Summary.NewProducts);
    }

    [Fact]
    public async Task Export_HidesPriceColumn_FromUsersDeniedReportsView()
    {
        await CommitAsync(Keeper, await ParseOkAsync(Keeper, Workbook(Sheet("ضلع آزمایشی", R("داروی آزمایشی ۱", "5", "TEST-B1", "31/05/2028")))));
        await App.AddKeeperAsync("keeper-noprice");
        using (var scope = App.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var user = (await users.FindByNameAsync("keeper-noprice"))!;
            Assert.True((await users.AddClaimAsync(user, new Claim(Permissions.UserDenyClaimType, Permissions.ReportsView))).Succeeded);
        }

        var client = await LoggedInAsync("keeper-noprice", TestApp.StaffPassword);
        var bytes = await (await client.GetAsync(ApiRoutes.DataExport)).Content.ReadAsByteArrayAsync();

        var header = XlsxReader.ReadAllSheets(new MemoryStream(bytes)).Single().Rows[0];
        Assert.DoesNotContain("قیمت", header);
        Assert.Contains("بارکد", header);
    }

    [Fact]
    public async Task Commit_RealisticVolume_700Rows_IsCorrectAndReasonablyFast()
    {
        // ~۶۲۳ قلم و ۷۰۴ ردیف مثل فایل واقعی؛ ۸۱ ردیف دومین بچ یک قلم. همه ساختگی.
        var rows = new List<ImportRowInput>();
        for (var i = 0; i < 623; i++)
            rows.Add(new() { Sheet = $"شیت {i % 8}", Name = $"TEST-DRUG-{i:000}", Brand = "TEST", UnitsPerPack = "30", Quantity = "3", BatchNumber = $"TEST-B{i:000}-1", Expiry = "31/05/2030" });
        for (var i = 0; i < 81; i++)
            rows.Add(new() { Sheet = $"شیت {i % 8}", Name = $"TEST-DRUG-{i:000}", Brand = "TEST", UnitsPerPack = "30", Quantity = "2", BatchNumber = $"TEST-B{i:000}-2", Expiry = "07/2031" });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var preview = (await (await Keeper.PostAsJsonAsync(ApiRoutes.ImportValidate, new ImportRequest { Rows = rows })).Content.ReadFromJsonAsync<ImportPreview>())!;
        Assert.True(preview.CanCommit);
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows });
        watch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<ImportResult>())!;
        Assert.Equal(623, result.ProductsCreated);
        Assert.Equal(704, result.BatchesCreated);
        Assert.Equal(8, result.LocationsCreated);
        await App.AssertLedgerConsistentAsync();
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(623 * 3 + 81 * 2, await db.Batches.SumAsync(b => b.OnHand));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"too slow: {watch.Elapsed}");
    }

    [Fact]
    public async Task Parse_HeaderWithoutVavInName_IsRecognised()
    {
        var rows = new List<string?[]> { new string?[] { "نام دارو دوز", "تعداد", "بچ نامبر", "تاریخ انقضا" }, new string?[] { "داروی آزمایشی ۱", "3", "TEST-B1", "31/05/2028" } };
        var bytes = Workbook(new XlsxSheet("ضلع آزمایشی", ["تاریخ بروزرسانی: آزمایشی"], rows));

        var preview = await ParseOkAsync(Keeper, bytes);

        Assert.True(preview.CanCommit);
        Assert.Equal("داروی آزمایشی ۱", Assert.Single(preview.Rows).Name);
    }

    private static ImportRowInput Bad() => new() { Sheet = "ضلع آزمایشی", RowNumber = 5, Name = "داروی بد", Quantity = "x", BatchNumber = "TEST-BAD", Expiry = "31/05/2028" };

    [Fact]
    public async Task Commit_ValidOnly_CommitsValidRows_AndSkipsRowsWithErrors()
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit,
            new ImportRequest { Rows = [Input("ضلع آزمایشی", "TEST-B1"), Bad(), Input("ضلع آزمایشی", "TEST-B2", "داروی آزمایشی دو")], ValidOnly = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<ImportResult>())!.BatchesCreated);
        using var scope = App.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
        Assert.Equal(["TEST-B1", "TEST-B2"], (await db.Batches.AsNoTracking().OrderBy(b => b.BatchNumber).ToListAsync()).Select(b => b.BatchNumber));
        Assert.False(await db.Products.AnyAsync(p => p.Name == "داروی بد"));
        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Commit_SelectedIndexes_CommitsOnlyThoseRows_AndTheRestCanBeCommittedLater()
    {
        var rows = new List<ImportRowInput> { Input("ضلع آزمایشی", "TEST-B1"), Input("ضلع آزمایشی", "TEST-B2", "داروی آزمایشی دو"), Input("ضلع آزمایشی", "TEST-B3", "داروی آزمایشی سه") };

        var first = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows, SelectedIndexes = [2, 0] });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var scope = App.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();
            Assert.Equal(["TEST-B1", "TEST-B3"], (await db.Batches.AsNoTracking().OrderBy(b => b.BatchNumber).ToListAsync()).Select(b => b.BatchNumber));
        }

        // ردیف‌های ثبت‌شده دوباره بفرستند: تکراری ← ۴۰۹ و چیزی عوض نمی‌شود؛ ردیف مانده هنوز ثبت‌شدنی است.
        Assert.Equal(HttpStatusCode.Conflict, (await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows, SelectedIndexes = [0, 1] })).StatusCode);
        var second = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows, SelectedIndexes = [1] });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using (var scope = App.CreateScope())
        {
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Batches.CountAsync());
        }

        await App.AssertLedgerConsistentAsync();
    }

    [Fact]
    public async Task Commit_SelectedRowWithError_Returns409_AndNothingIsCommitted()
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit,
            new ImportRequest { Rows = [Input("ضلع آزمایشی", "TEST-B1"), Bad()], SelectedIndexes = [0, 1] });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = App.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Batches.CountAsync());
    }

    [Fact]
    public async Task Commit_SelectedDuplicateBatchInSubset_IsRevalidated_AndRejected()
    {
        var rows = new List<ImportRowInput> { Input("ضلع آزمایشی", "TEST-B1"), Input("ضلع آزمایشی", "TEST-B1") };

        // هر دو انتخاب‌شده ← بچ تکراری در همان فایل ← ۴۰۹؛ یکی به‌تنهایی درست است.
        Assert.Equal(HttpStatusCode.Conflict, (await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows, SelectedIndexes = [0, 1] })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = rows, SelectedIndexes = [1] })).StatusCode);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public async Task Commit_SelectedIndexOutOfRange_Returns400(int index)
    {
        var response = await Keeper.PostAsJsonAsync(ApiRoutes.ImportCommit, new ImportRequest { Rows = [Input("ضلع آزمایشی", "TEST-B1")], SelectedIndexes = [index] });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Commit_ExcelZeroDateRow_IsNotCommittedAsExpiredBatch()
    {
        var row = Input("ضلع آزمایشی", "TEST-B1");
        row.Expiry = "31/12/1899";

        Assert.Equal(HttpStatusCode.Conflict, (await CommitRowsAsync(row)).StatusCode);
    }
}
