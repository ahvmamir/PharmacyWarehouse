using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// ورود جنس: حرکت Receipt و به‌روزرسانی OnHand بچ در یک SaveChanges (یک تراکنش).
// ثبت تکی و گروهی یک قاعده دارند (ApplyLineAsync)؛ گروهی فقط تا آخرین ردیف SaveChanges نمی‌زند، پس همه یا هیچ است.
public static class ReceiptEndpoints
{
    private const string TwoDates = "یک بچ دو تاریخ ندارد؛ بچ نامبر را بررسی کنید.";
    private const string BatchExists = "این بچ قبلاً ثبت شده است؛ آن را از فهرست بچ‌ها انتخاب کنید.";
    private const string RaceMessage = "این بچ هم‌زمان ثبت شد؛ دوباره تلاش کنید.";
    private const string TwoLocations = "یک بچ تازه در دو ردیف با محل متفاوت آمده است.";
    private const string VersionChanged = "این بچ در این فاصله تغییر کرده است. دوباره بارگذاری شد؛ بررسی کنید.";

    public static void MapReceiptEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Receipts).RequireAuthorization(Permissions.StockReceive);
        group.MapPost("", ReceiveAsync);
        group.MapPost("bulk", ReceiveBulkAsync);
    }

    private enum FailureKind { Validation, NotFound, Conflict, Version }

    private sealed record LineFailure(FailureKind Kind, string Field, string Message);

    private sealed record AppliedLine(Batch Batch, Guid? ProductLocationId, bool Created, IReadOnlyList<ExpiryWarning> Warnings);

    // آنچه در یک درخواست بین ردیف‌ها مشترک است. NewBatches لازم است چون بچ تازه تا SaveChanges در دیتابیس نیست.
    // CanDefineProducts: تعریف قلم تازه داخل ورود همان مجوز «تعریف قلم» (products.manage) را می‌خواهد.
    private sealed class ReceiptSession(WarehouseDbContext db, StockLedger ledger, List<StorageLocation> locations, bool canDefineProducts)
    {
        public WarehouseDbContext Db { get; } = db;
        public bool CanDefineProducts { get; } = canDefineProducts;
        // قلم‌های تازه‌ی همین درخواست با کلید «نام|برند|بسته»، و بارکدهای تازه با ScanKey → قلم.
        public Dictionary<string, Product> NewProducts { get; } = [];
        public Dictionary<string, Product> NewBarcodes { get; } = [];
        public StockLedger Ledger { get; } = ledger;
        public List<StorageLocation> Locations { get; } = locations;
        public HashSet<Guid> Usable { get; } =
            LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive))).ToHashSet();
        public Dictionary<(Guid Product, string Key), Batch> NewBatches { get; } = [];
        public DateTime Now { get; } = DateTime.UtcNow;

        public IReadOnlyDictionary<Guid, string> Paths() =>
            LocationTree.Paths(Locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
    }

    private static async Task<ReceiptSession> OpenSessionAsync(WarehouseDbContext db, StockLedger ledger, AuditContext audit, PermissionService permissions) =>
        new(db, ledger, await db.StorageLocations.AsNoTracking().ToListAsync(),
            await permissions.HasAsync(audit.UserId, Permissions.ProductsManage));

    // قلم تازه‌ی یک ردیف. خطاها با پیشوند «NewProduct.» برمی‌گردند تا کلاینت کنار همان فیلد نشان دهد.
    private static async Task<(Product? Product, LineFailure? Failure)> NewProductAsync(SaveProductRequest request, string? barcode, ReceiptSession session)
    {
        static (Product?, LineFailure?) Fail(FailureKind kind, string field, string message) => (null, new LineFailure(kind, field, message));

        if (!session.CanDefineProducts) return Fail(FailureKind.Validation, "NewProduct", "اجازه‌ی تعریف قلم تازه را ندارید؛ قلم را از جست‌وجو انتخاب کنید.");
        if (ProductRules.Validate(request) is [var first, ..]) return Fail(FailureKind.Validation, $"NewProduct.{first.Field}", first.Message);

        var db = session.Db;
        if (request.LocationId is { } locationId)
        {
            if (session.Locations.All(l => l.Id != locationId)) return Fail(FailureKind.Validation, "NewProduct.LocationId", "محل پیدا نشد.");
            if (!session.Usable.Contains(locationId)) return Fail(FailureKind.Validation, "NewProduct.LocationId", "این محل غیرفعال است.");
        }

        if (request.CategoryId is { } categoryId)
        {
            var category = await db.ProductCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
            if (category is null) return Fail(FailureKind.Validation, "NewProduct.CategoryId", "دسته پیدا نشد.");
            if (!category.IsActive) return Fail(FailureKind.Validation, "NewProduct.CategoryId", "این دسته غیرفعال است.");
        }

        var candidate = new Product { Id = Guid.NewGuid(), CreatedAtUtc = session.Now };
        ProductEndpoints.Apply(candidate, request);
        var key = $"{candidate.NameKey}|{candidate.BrandKey}|{candidate.PackKey}";

        // همان قلم تازه در ردیف دیگرِ همین درخواست (مثلاً بچ دوم) یک قلم است، نه دو قلم.
        var isNew = !session.NewProducts.TryGetValue(key, out var product);
        if (isNew)
        {
            if (await db.Products.AnyAsync(p => p.NameKey == candidate.NameKey && p.BrandKey == candidate.BrandKey && p.PackKey == candidate.PackKey))
            {
                return Fail(FailureKind.Conflict, "NewProduct.Name", ProductEndpoints.DuplicateProduct + " آن را از جست‌وجو انتخاب کنید.");
            }

            product = candidate;
        }

        var code = Gs1Scan.StoredCode(barcode);
        if (code is not null && TextNormalizer.ScanKey(code) is { } codeKey)
        {
            var variants = Gs1Scan.CodeVariants(codeKey);
            if (code.Length > ProductRules.MaxCode) return Fail(FailureKind.Validation, "NewBarcode", $"بارکد حداکثر {ProductRules.MaxCode} نویسه است.");

            var owner = await db.ProductBarcodes.AsNoTracking().Where(b => variants.Contains(b.CodeKey))
                .Join(db.Products, b => b.ProductId, p => p.Id, (b, p) => p.Name).FirstOrDefaultAsync();
            if (owner is not null) return Fail(FailureKind.Conflict, "NewBarcode", $"این بارکد مال «{owner}» است.");

            if (session.NewBarcodes.TryGetValue(codeKey, out var holder))
            {
                if (holder != product) return Fail(FailureKind.Conflict, "NewBarcode", $"این بارکد در ردیف دیگر برای «{holder.Name}» آمده است.");
            }
            else
            {
                db.ProductBarcodes.Add(new ProductBarcode { Id = Guid.NewGuid(), ProductId = product!.Id, Code = code, CodeKey = codeKey });
                session.NewBarcodes[codeKey] = product;
            }
        }

        if (isNew)
        {
            db.Products.Add(product!);
            session.NewProducts[key] = product!;
        }

        product!.SearchText = ProductEndpoints.BuildSearchText(product,
            db.ProductBarcodes.Local.Where(b => b.ProductId == product.Id).Select(b => b.Code));
        return (product, null);
    }

    private static async Task<(AppliedLine? Line, LineFailure? Failure)> ApplyLineAsync(ReceiptRequest request, ReceiptSession session)
    {
        static (AppliedLine?, LineFailure?) Fail(FailureKind kind, string field, string message) => (null, new LineFailure(kind, field, message));

        var batchNumber = TextNormalizer.Clean(request.BatchNumber);
        var batchKey = TextNormalizer.ScanKey(batchNumber);
        if (batchNumber is null || batchKey is null) return Fail(FailureKind.Validation, "BatchNumber", "بچ نامبر را وارد کنید.");
        if (batchNumber.Length > ProductRules.MaxBatch) return Fail(FailureKind.Validation, "BatchNumber", $"بچ نامبر حداکثر {ProductRules.MaxBatch} نویسه است.");
        if (request.Quantity <= 0) return Fail(FailureKind.Validation, "Quantity", "تعداد باید بیشتر از صفر باشد.");
        if (request.UnitCost is < 0) return Fail(FailureKind.Validation, "UnitCost", "قیمت واحد نمی‌تواند منفی باشد.");
        var note = TextNormalizer.Clean(request.Note);
        if (note is { Length: > ProductRules.MaxNote }) return Fail(FailureKind.Validation, "Note", $"یادداشت حداکثر {ProductRules.MaxNote} نویسه است.");

        var expiry = ExpiryDate.Parse(request.Expiry);
        if (expiry.Error is { } error) return Fail(FailureKind.Validation, "Expiry", error);

        var db = session.Db;
        Product product;
        if (request.ProductId == Guid.Empty && request.NewProduct is { } newProduct)
        {
            var (newOne, failure) = await NewProductAsync(newProduct, request.NewBarcode, session);
            if (failure is not null) return (null, failure);
            product = newOne!;
        }
        else
        {
            var found = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId);
            if (found is null) return Fail(FailureKind.NotFound, "ProductId", "این قلم پیدا نشد.");
            if (!found.IsActive) return Fail(FailureKind.Validation, "ProductId", "این قلم غیرفعال است.");
            product = found;
        }

        if (request.LocationId is { } locationId)
        {
            if (session.Locations.All(l => l.Id != locationId)) return Fail(FailureKind.Validation, "LocationId", "محل پیدا نشد.");
            if (!session.Usable.Contains(locationId)) return Fail(FailureKind.Validation, "LocationId", "این محل غیرفعال است.");
        }

        Batch batch;
        var created = false;
        if (session.NewBatches.TryGetValue((product.Id, batchKey), out var earlier))
        {
            // همان بچ تازه در ردیف قبلیِ همین درخواست: تعداد جمع می‌شود، ولی تاریخ یا محل متفاوت نقض قاعده است.
            if (expiry.Value is { } given && earlier.ExpiryDate != given.Date) return Fail(FailureKind.Conflict, "Expiry", TwoDates);
            if (request.LocationId != earlier.LocationId) return Fail(FailureKind.Conflict, "LocationId", TwoLocations);
            batch = earlier;
        }
        else if (await db.Batches.FirstOrDefaultAsync(b => b.ProductId == product.Id && b.BatchKey == batchKey) is { } existing)
        {
            // تاریخ خالی = همان تاریخ ثبت‌شده؛ هر تاریخ دیگر (حتی وقتی بچ تاریخ ندارد) ناسازگار است.
            if (expiry.Value is { } given && existing.ExpiryDate != given.Date) return Fail(FailureKind.Conflict, "Expiry", TwoDates);
            // بدون Version یعنی Client «بچ تازه» فرستاده ولی شماره موجود است؛ پیام کهنه‌بودن گمراه‌کننده است.
            if (request.BatchVersion is not { } version) return Fail(FailureKind.Conflict, "BatchNumber", BatchExists);
            // ناسازگاری پیش از ذخیره سنجیده می‌شود تا ردیف مقصر معلوم باشد؛ ExpectVersion جلوی رقابت پس از این خواندن را می‌گیرد.
            if (existing.Version != version) return Fail(FailureKind.Version, "BatchVersion", VersionChanged);
            db.ExpectVersion(existing, version);
            batch = existing;
        }
        else
        {
            if (expiry.Value is null && product.RequiresExpiry) return Fail(FailureKind.Validation, "Expiry", "تاریخ انقضا را وارد کنید.");

            batch = new Batch
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                BatchNumber = batchNumber,
                BatchKey = batchKey,
                ExpiryDate = expiry.Value?.Date,
                ExpiryPrecision = expiry.Value?.Precision ?? ExpiryPrecision.Day,
                ExpiryCalendar = expiry.Value?.Calendar ?? CalendarKind.Gregorian,
                LocationId = request.LocationId, // null ← محل قلم
                UnitCost = request.UnitCost,
                CreatedAtUtc = session.Now
            };
            db.Batches.Add(batch);
            session.NewBatches[(product.Id, batchKey)] = batch;
            created = true;
        }

        session.Ledger.Post(batch, StockMovementKind.Receipt, request.Quantity, session.Now, note);
        return (new AppliedLine(batch, product.LocationId, created, expiry.Warnings), null);
    }

    private static async Task<IResult> ReceiveAsync(ReceiptRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit, PermissionService permissions)
    {
        var session = await OpenSessionAsync(db, ledger, audit, permissions);
        var (line, failure) = await ApplyLineAsync(request, session);
        if (failure is not null)
        {
            return failure.Kind switch
            {
                FailureKind.NotFound => ApiProblems.NotFound(),
                FailureKind.Conflict => ApiProblems.Conflict(failure.Message),
                FailureKind.Version => ApiProblems.VersionConflict(),
                _ => ApiProblems.Validation(failure.Field, failure.Message)
            };
        }

        if (await SaveAsync(db) is { } problem) return problem;

        return Results.Ok(new ReceiptResult(ToItem(line!, session.Paths()), line!.Created, line.Warnings));
    }

    private static async Task<IResult> ReceiveBulkAsync(BulkReceiptRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit, PermissionService permissions)
    {
        if (request.Lines is not { Count: > 0 } lines) return ApiProblems.Validation("Lines", "هیچ ردیفی برای ثبت نیست.");
        if (lines.Count > BulkReceiptRequest.MaxLines) return ApiProblems.Validation("Lines", $"حداکثر {BulkReceiptRequest.MaxLines} ردیف در یک ثبت ممکن است.");

        var session = await OpenSessionAsync(db, ledger, audit, permissions);
        var applied = new List<AppliedLine>(lines.Count);
        var errors = new List<ValidationError>();
        var conflict = false;
        var version = false;

        // ردیف خطادار متوقف نمی‌کند: کاربر همه‌ی خطاها را یک‌جا می‌بیند. ذخیره فقط اگر هیچ خطایی نباشد.
        for (var i = 0; i < lines.Count; i++)
        {
            var (line, failure) = await ApplyLineAsync(lines[i], session);
            if (failure is null)
            {
                applied.Add(line!);
                continue;
            }

            errors.Add(new ValidationError(BulkReceiptErrors.Key(i, failure.Field), failure.Message));
            conflict |= failure.Kind == FailureKind.Conflict;
            version |= failure.Kind == FailureKind.Version;
        }

        if (errors.Count > 0) return ApiProblems.LineErrors(errors, conflict, version);
        if (await SaveAsync(db) is { } problem) return problem;

        var paths = session.Paths();
        return Results.Ok(new BulkReceiptResult(applied.Select(l => new ReceiptResult(ToItem(l, paths), l.Created, l.Warnings)).ToList()));
    }

    private static BatchItem ToItem(AppliedLine line, IReadOnlyDictionary<Guid, string> paths) =>
        BatchMapping.ToItem(line.Batch, paths, line.ProductLocationId, BatchMapping.Today());

    private static async Task<IResult?> SaveAsync(WarehouseDbContext db)
    {
        try
        {
            await db.SaveChangesAsync();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict(RaceMessage);
        }
    }
}
