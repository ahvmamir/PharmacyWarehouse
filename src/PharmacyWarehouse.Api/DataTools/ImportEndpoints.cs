using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Stock;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Import;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.DataTools;

// ورود و خروج Excel (بخش ۱۲ spec). مسیر: parse ← اصلاح در مرورگر ← validate ← commit؛ هر دو commit و validate از ImportValidator می‌گذرند.
public static class ImportEndpoints
{
    private const long MaxFileBytes = 10 * 1024 * 1024;
    private const string ImportReason = "ورود از Excel";

    // سرستون‌های قالب؛ ترتیب همین است. قیمت فقط برای reports.view.
    private static readonly string[] Columns =
        ["نام دارو و دوز", "نام تجاری", "تعداد", "تعداد در بسته", "بچ نامبر", "تاریخ انقضا", "محل", "بارکد", "حداقل موجودی", "قیمت"];

    public static void MapImportEndpoints(this WebApplication app)
    {
        var import = app.MapGroup("/api/import").RequireAuthorization(Permissions.DataImport);
        // CSRF با هدر سفارشی ما انجام می‌شود (CsrfHeaderMiddleware)؛ antiforgery فرم پیش‌فرض ASP.NET برای فایل لازم نیست.
        import.MapPost("parse", ParseAsync).DisableAntiforgery();
        import.MapPost("validate", ValidateAsync);
        import.MapPost("commit", CommitAsync);
        import.MapGet("template", Template);

        app.MapGet(ApiRoutes.DataExport, ExportAsync).RequireAuthorization(Permissions.DataExport);
    }

    // ---------- پیش‌نمایش ----------

    private static async Task<IResult> ParseAsync(IFormFile? file, WarehouseDbContext db)
    {
        if (file is null || file.Length == 0) return ApiProblems.Validation("File", "فایل Excel را انتخاب کنید.");
        if (file.Length > MaxFileBytes) return ApiProblems.Validation("File", "فایل بیش از ۱۰ مگابایت است.");

        List<XlsxSheetData> sheets;
        try
        {
            await using var stream = file.OpenReadStream();
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);
            copy.Position = 0;
            sheets = XlsxReader.ReadAllSheets(copy);
        }
        catch (InvalidDataException)
        {
            return ApiProblems.Validation("File", "فایل Excel (xlsx) معتبر نیست.");
        }

        var rows = new List<ImportRowInput>();
        var ignored = 0;
        var anyHeader = false;
        foreach (var sheet in sheets)
        {
            if (!TryMapHeader(sheet.Rows, out var headerIndex, out var map)) continue;
            anyHeader = true;

            for (var r = headerIndex + 1; r < sheet.Rows.Count; r++)
            {
                string? Cell(string field) => map.TryGetValue(field, out var c) && c < sheet.Rows[r].Length ? sheet.Rows[r][c] : null;
                var name = TextNormalizer.Clean(Cell("name"))?.Trim('*').Trim();
                if (string.IsNullOrEmpty(name))
                {
                    // ردیف کاملاً خالی نشمرده می‌شود؛ ردیف‌های قالب فقط «*» دارند.
                    if (sheet.Rows[r].Any(c => !string.IsNullOrWhiteSpace(c))) ignored++;
                    continue;
                }

                rows.Add(new ImportRowInput
                {
                    Sheet = sheet.Name, RowNumber = r + 1, Name = Cell("name"), Brand = Cell("brand"), Quantity = Cell("quantity"),
                    UnitsPerPack = Cell("pack"), BatchNumber = Cell("batch"), Expiry = Cell("expiry"), Location = Cell("location"),
                    Barcode = Cell("barcode"), MinStock = Cell("minstock"), UnitCost = Cell("cost")
                });
                if (rows.Count > ImportValidator.MaxRows) return ApiProblems.Validation("File", $"بیش از {ImportValidator.MaxRows} ردیف؛ فایل را تقسیم کنید.");
            }
        }

        if (!anyHeader) return ApiProblems.Validation("File", "هیچ شیتی با سرستون «نام دارو» پیدا نشد؛ از قالب برنامه استفاده کنید.");

        return Results.Ok(await PreviewAsync(db, rows, ignored));
    }

    private static async Task<IResult> ValidateAsync(ImportRequest request, WarehouseDbContext db)
    {
        if (request.Rows is null) return ApiProblems.Validation("Rows", "ردیفی فرستاده نشد.");
        if (request.Rows.Count > ImportValidator.MaxRows) return ApiProblems.Validation("Rows", $"بیش از {ImportValidator.MaxRows} ردیف.");
        return Results.Ok(await PreviewAsync(db, request.Rows, 0));
    }

    private static async Task<ImportPreview> PreviewAsync(WarehouseDbContext db, IReadOnlyList<ImportRowInput> rows, int ignored)
    {
        var context = await BuildContextAsync(db);
        var report = ImportValidator.Validate(rows, context);
        return new ImportPreview(rows, report.Statuses, report.Summary, ignored + report.Summary.IgnoredRows, report.CanCommit);
    }

    // نگاشت سرستون‌ها: سطر سرستون جزو ۵ ردیف اول است و ستون «نام دارو» دارد؛ ستون‌های ناشناخته (مثل «بازرسی دوره‌ای») نادیده‌اند.
    private static readonly (string Field, string[] Names)[] HeaderAliases =
    [
        ("name", ["نامداروودوز", "نامدارو", "نام", "دارو", "نامقلم"]),
        ("brand", ["نامتجاری", "برند"]),
        ("quantity", ["تعداد", "موجودی"]),
        ("pack", ["تعداددربسته", "بستهبندی", "دربسته"]),
        ("batch", ["بچنامبر", "بچ", "شمارهبچ", "شمارهیبچ"]),
        ("expiry", ["تاریخانقضا", "انقضا"]),
        ("location", ["محل"]),
        ("barcode", ["بارکد"]),
        ("minstock", ["حداقلموجودی", "حداقل"]),
        ("cost", ["قیمت", "قیمتواحد"])
    ];

    private static string HeaderKey(string? text) =>
        (TextNormalizer.Key(text) ?? "").Replace("‌", "").Replace("-", "");

    private static bool TryMapHeader(List<string?[]> rows, out int headerIndex, out Dictionary<string, int> map)
    {
        for (var r = 0; r < Math.Min(rows.Count, 5); r++)
        {
            var candidate = new Dictionary<string, int>();
            for (var c = 0; c < rows[r].Length; c++)
            {
                var key = HeaderKey(rows[r][c]);
                if (key.Length == 0) continue;
                foreach (var (field, names) in HeaderAliases)
                {
                    if (!candidate.ContainsKey(field) && names.Contains(key)) { candidate[field] = c; break; }
                }
            }

            if (candidate.ContainsKey("name")) { headerIndex = r; map = candidate; return true; }
        }

        headerIndex = -1;
        map = [];
        return false;
    }

    internal static async Task<ImportContext> BuildContextAsync(WarehouseDbContext db)
    {
        var context = new ImportContext();
        string KeyOf(Product p) => ImportValidator.ProductKeyOf(p.Name, p.Brand, p.PackDescription);

        var products = await db.Products.AsNoTracking().ToListAsync();
        var byId = products.ToDictionary(p => p.Id, KeyOf);
        foreach (var p in products) context.ExistingProducts[byId[p.Id]] = p.RequiresExpiry;
        foreach (var b in await db.Batches.AsNoTracking().Select(b => new { b.ProductId, b.BatchKey }).ToListAsync())
            context.ExistingBatches.Add(ImportValidator.BatchKeyOf(byId[b.ProductId], b.BatchKey));
        foreach (var c in await db.ProductBarcodes.AsNoTracking().Select(c => new { c.ProductId, c.CodeKey }).ToListAsync())
            context.ExistingBarcodes[c.CodeKey] = byId[c.ProductId];

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        foreach (var l in locations.Where(l => l.ParentId is null)) context.RootLocationKeys.Add(l.NameKey);
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        foreach (var path in paths.Values) if (ImportValidator.LocationKey(path) is { } key) context.LocationPathKeys.Add(key);
        return context;
    }

    // ---------- ثبت ----------

    private static async Task<IResult> CommitAsync(ImportRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (request.Rows is null || request.Rows.Count == 0) return ApiProblems.Validation("Rows", "ردیفی برای ثبت نیست.");
        if (request.Rows.Count > ImportValidator.MaxRows) return ApiProblems.Validation("Rows", $"بیش از {ImportValidator.MaxRows} ردیف.");

        var report = ImportValidator.Validate(request.Rows, await BuildContextAsync(db));
        if (!report.CanCommit)
            return ApiProblems.Conflict(report.Summary.ErrorRows > 0
                ? "ردیف‌های دارای خطا باقی مانده‌اند؛ اول اصلاح و دوباره بررسی کنید."
                : "ردیفی برای ثبت نیست.");

        audit.Reason = ImportReason;
        var now = DateTime.UtcNow;

        var locations = await db.StorageLocations.ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        var byPath = new Dictionary<string, Guid>();
        foreach (var (id, path) in paths) if (ImportValidator.LocationKey(path) is { } key) byPath[key] = id;
        var created = new List<StorageLocation>();

        // زنجیره‌ی محل (نام شیت یا مسیر ستون «محل») را می‌یابد و بخش‌های ناموجود را می‌سازد.
        Guid? EnsureChain(string path)
        {
            Guid? parent = null;
            var accumulated = "";
            foreach (var segment in path.Split(['›', '/', '>'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var segmentKey = TextNormalizer.Key(segment)!;
                accumulated = accumulated.Length == 0 ? segmentKey : accumulated + "›" + segmentKey;
                if (byPath.TryGetValue(accumulated, out var found)) { parent = found; continue; }

                var location = new StorageLocation
                {
                    Id = Guid.NewGuid(), Name = TextNormalizer.Clean(segment)!, NameKey = segmentKey, ParentId = parent,
                    ParentKey = StorageLocation.KeyOf(parent), SortOrder = locations.Count + created.Count
                };
                created.Add(location);
                db.StorageLocations.Add(location);
                byPath[accumulated] = location.Id;
                parent = location.Id;
            }

            return parent;
        }

        Guid? ResolveLocation(ImportPlanRow row) =>
            row.LocationPath is not null ? EnsureChain(row.LocationPath)
            : TextNormalizer.Clean(row.Sheet) is { } sheet ? EnsureChain(sheet.Replace('/', ' ').Replace('>', ' ').Replace('›', ' ')) : null;

        string KeyOf(Product p) => ImportValidator.ProductKeyOf(p.Name, p.Brand, p.PackDescription);
        var productIds = (await db.Products.AsNoTracking().ToListAsync()).ToDictionary(KeyOf, p => p.Id);
        var newProducts = new HashSet<Guid>();
        var existing = new HashSet<Guid>();
        var batches = 0;
        var barcodes = 0;
        var order = 0;

        foreach (var row in report.Plan)
        {
            if (!productIds.TryGetValue(row.ProductKey, out var productId))
            {
                var product = new Product
                {
                    Id = Guid.NewGuid(),
                    Name = row.Name,
                    NameKey = TextNormalizer.Key(row.Name)!,
                    Brand = row.Brand,
                    BrandKey = TextNormalizer.Key(row.Brand) ?? "",
                    PackDescription = row.Pack,
                    PackKey = TextNormalizer.Key(row.Pack) ?? "",
                    LocationId = ResolveLocation(row),
                    MinStock = row.MinStock,
                    RequiresExpiry = row.RequiresExpiry,
                    CreatedAtUtc = now
                };
                var codes = new List<string>();
                if (row.Barcode is not null && row.BarcodeKey is not null)
                {
                    db.ProductBarcodes.Add(new ProductBarcode { Id = Guid.NewGuid(), ProductId = product.Id, Code = row.Barcode, CodeKey = row.BarcodeKey });
                    codes.Add(row.Barcode);
                    barcodes++;
                }

                product.SearchText = ProductEndpoints.BuildSearchText(product, codes);
                db.Products.Add(product);
                productIds[row.ProductKey] = productId = product.Id;
                newProducts.Add(productId);
            }
            else if (!newProducts.Contains(productId))
            {
                existing.Add(productId);
            }

            if (row.Quantity <= 0) continue;

            var batch = new Batch
            {
                Id = Guid.NewGuid(), ProductId = productId, BatchNumber = row.BatchNumber!, BatchKey = row.BatchKey!,
                ExpiryDate = row.Expiry?.Date, ExpiryPrecision = row.Expiry?.Precision ?? ExpiryPrecision.Day,
                ExpiryCalendar = row.Expiry?.Calendar ?? CalendarKind.Gregorian, UnitCost = row.UnitCost,
                // به ترتیب ردیف جلو می‌رود تا FEFO بین بچ‌های همتاریخ پایدار باشد.
                CreatedAtUtc = now.AddTicks(order++)
            };
            db.Batches.Add(batch);
            ledger.Post(batch, StockMovementKind.Import, row.Quantity, now, ImportReason);
            batches++;
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict("هم‌زمان با ورود دیگری تکراری شد؛ دوباره بررسی کنید.");
        }

        return Results.Ok(new ImportResult(newProducts.Count, existing.Count, batches, created.Count, barcodes));
    }

    // ---------- قالب و خروجی ----------

    private static IResult Template()
    {
        var bytes = XlsxWriter.Write(
            new XlsxSheet("اقلام", Columns, [["داروی آزمایشی 10mg", "برند آزمایشی", "5", "30", "TEST-B1", "31/05/2028", "", "TEST-0001", "2", ""]],
                NumericColumns: new HashSet<int> { 2, 8, 9 }),
            new XlsxSheet("راهنما", ["راهنما"], GuideLines().Select(l => new string?[] { l })));
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "pharmacywarehouse-import-template.xlsx");
    }

    private static IEnumerable<string> GuideLines() =>
    [
        "هر شیت یک محل یا گروه است؛ نام شیت اگر محلی با آن نام نباشد، ساخته می‌شود.",
        "ستون‌های اجباری: نام دارو و دوز، تعداد. تعداد ۰ یعنی فقط قلم تعریف شود.",
        "تاریخ انقضا: روز/ماه/سال یا ماه/سال، میلادی یا شمسی (سال کمتر از ۱۵۰۰ شمسی است).",
        "ستون‌های «محل»، «بارکد»، «حداقل موجودی» و «قیمت» اختیاری‌اند. بارکد و حداقل موجودی فقط برای قلم تازه اعمال می‌شود.",
        "بچی که از قبل در سیستم هست خطا می‌دهد؛ ورود Excel موجودی را جایگزین نمی‌کند."
    ];

    private static async Task<IResult> ExportAsync(WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        var withPrice = await permissions.HasAsync(audit.UserId, Permissions.ReportsView);
        var columns = withPrice ? Columns : Columns[..^1];

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        string? PathOf(Guid? id) => id is { } g && paths.TryGetValue(g, out var p) ? p : null;

        var products = (await db.Products.AsNoTracking().ToListAsync()).OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Brand, StringComparer.Ordinal).ThenBy(p => p.Id).ToList();
        var batches = (await db.Batches.AsNoTracking().ToListAsync()).ToLookup(b => b.ProductId);
        var codes = (await db.ProductBarcodes.AsNoTracking().ToListAsync()).ToLookup(c => c.ProductId);

        var rows = new List<string?[]>();
        foreach (var p in products)
        {
            var code = codes[p.Id].OrderBy(c => c.Code, StringComparer.Ordinal).FirstOrDefault()?.Code;
            var list = batches[p.Id].OrderBy(b => b.ExpiryDate is null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.CreatedAtUtc).ThenBy(b => b.Id).ToList();
            if (list.Count == 0) list.Add(null!);

            foreach (var b in list)
            {
                var row = new List<string?>
                {
                    p.Name, p.Brand, (b?.OnHand ?? 0).ToString(), p.PackDescription, b?.BatchNumber,
                    b?.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null,
                    PathOf(b?.LocationId ?? p.LocationId), code, p.MinStock?.ToString()
                };
                if (withPrice) row.Add(b?.UnitCost?.ToString());
                rows.Add(row.ToArray());
            }
        }

        var numeric = withPrice ? new HashSet<int> { 2, 8, 9 } : new HashSet<int> { 2, 8 };
        var bytes = XlsxWriter.Write(new XlsxSheet("موجودی", columns, rows, numeric));
        return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"pharmacywarehouse-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }
}
