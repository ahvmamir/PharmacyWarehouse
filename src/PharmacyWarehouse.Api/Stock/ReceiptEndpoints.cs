using Microsoft.EntityFrameworkCore;
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
    private sealed class ReceiptSession(WarehouseDbContext db, StockLedger ledger, List<StorageLocation> locations)
    {
        public WarehouseDbContext Db { get; } = db;
        public StockLedger Ledger { get; } = ledger;
        public List<StorageLocation> Locations { get; } = locations;
        public HashSet<Guid> Usable { get; } =
            LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive))).ToHashSet();
        public Dictionary<(Guid Product, string Key), Batch> NewBatches { get; } = [];
        public DateTime Now { get; } = DateTime.UtcNow;

        public IReadOnlyDictionary<Guid, string> Paths() =>
            LocationTree.Paths(Locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
    }

    private static async Task<ReceiptSession> OpenSessionAsync(WarehouseDbContext db, StockLedger ledger) =>
        new(db, ledger, await db.StorageLocations.AsNoTracking().ToListAsync());

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
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return Fail(FailureKind.NotFound, "ProductId", "این قلم پیدا نشد.");
        if (!product.IsActive) return Fail(FailureKind.Validation, "ProductId", "این قلم غیرفعال است.");

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

    private static async Task<IResult> ReceiveAsync(ReceiptRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        var session = await OpenSessionAsync(db, ledger);
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

    private static async Task<IResult> ReceiveBulkAsync(BulkReceiptRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        if (request.Lines is not { Count: > 0 } lines) return ApiProblems.Validation("Lines", "هیچ ردیفی برای ثبت نیست.");
        if (lines.Count > BulkReceiptRequest.MaxLines) return ApiProblems.Validation("Lines", $"حداکثر {BulkReceiptRequest.MaxLines} ردیف در یک ثبت ممکن است.");

        var session = await OpenSessionAsync(db, ledger);
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
