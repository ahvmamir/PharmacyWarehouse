using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// بررسی ورود جنس ساده توسط انباردار (بخش ۱۵.۲ spec). حرکت هرگز ویرایش نمی‌شود:
// تغییر تعداد = حرکت تفاوت (Adjustment)، رد = Reversal برای هر ردیف.
public static class ReceiptReviewEndpoints
{
    private const int MaxListTake = 200;
    private const string AlreadyReviewed = "این ورود قبلاً بررسی شده است.";
    private const string AdjustNote = "اصلاح ورود";
    private const string StockGone = "موجودی این بچ کمتر از مقدار لازم است؛ بخشی از آن برداشته شده.";

    public static void MapReceiptReviewEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Receipts).RequireAuthorization(Permissions.StockReceive);
        group.MapGet("pending", PendingAsync);
        group.MapPost("{id:guid}/review", SaveAsync);
        group.MapPost("{id:guid}/approve", ApproveAsync);
        group.MapPost("{id:guid}/reject", RejectAsync);
    }

    private static async Task<IResult> PendingAsync(WarehouseDbContext db, int take = 100)
    {
        take = Math.Clamp(take, 1, MaxListTake);
        var page = await db.Receipts.AsNoTracking().Where(r => r.Status == ReceiptStatus.Pending)
            .OrderBy(r => r.AtUtc).ThenBy(r => r.Id).Take(take).ToListAsync();
        return Results.Ok(await QuickReceiptEndpoints.ToItemsAsync(db, page));
    }

    private static async Task<(Receipt? Receipt, IResult? Problem)> LoadPendingAsync(WarehouseDbContext db, Guid id, int version)
    {
        var receipt = await db.Receipts.FirstOrDefaultAsync(r => r.Id == id);
        if (receipt is null) return (null, ApiProblems.NotFound());
        if (receipt.Status != ReceiptStatus.Pending) return (null, ApiProblems.Conflict(AlreadyReviewed));

        db.ExpectVersion(receipt, version);
        return (receipt, null);
    }

    private static async Task<IResult> SaveChangesAsync(WarehouseDbContext db, Receipt receipt)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict("نام قلم با قلم دیگری یکی می‌شود یا هم‌زمان تغییر کرد.");
        }

        return Results.Ok((await QuickReceiptEndpoints.ToItemsAsync(db, [receipt])).Single());
    }

    private static async Task<IResult> SaveAsync(Guid id, ReviewReceiptRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        var (receipt, problem) = await LoadPendingAsync(db, id, request.Version);
        if (problem is not null) return problem;

        if (request.Lines is not { Count: > 0 } edits) return ApiProblems.Validation("Lines", "تغییری برای ثبت نیست.");
        if (edits.Select(e => e.LineId).Distinct().Count() != edits.Count) return ApiProblems.Validation("Lines", "یک ردیف فقط یک بار می‌آید.");

        var lines = await db.ReceiptLines.Where(l => l.ReceiptId == id).ToDictionaryAsync(l => l.Id);
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var usable = LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive))).ToHashSet();
        var versionedBatches = new HashSet<Guid>();
        var errors = new List<ValidationError>();
        var conflict = false;
        var version = false;

        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits[i];
            void Error(string field, string message, bool isConflict = false)
            {
                errors.Add(new ValidationError(BulkReceiptErrors.Key(i, field), message));
                conflict |= isConflict;
            }

            if (!lines.TryGetValue(edit.LineId, out var line)) { Error("LineId", "این ردیف پیدا نشد."); continue; }
            if (edit.Quantity <= 0) { Error("Quantity", "تعداد باید بیشتر از صفر باشد."); continue; }
            if (edit.Product is not null && edit.MergeIntoProductId is not null) { Error("Product", "ویرایش قلم و ادغام با هم ممکن نیست."); continue; }

            db.ExpectVersion(line, edit.LineVersion);
            var batch = await db.Batches.FirstAsync(b => b.Id == line.BatchId);
            if (batch.Version != edit.BatchVersion) { Error("BatchVersion", "این بچ در این فاصله تغییر کرده است؛ دوباره بارگذاری کنید.", true); version = true; continue; }

            // تعداد: حرکت تفاوت؛ کاهش بیش از موجودی بچ یعنی بخشی برداشته شده.
            var difference = edit.Quantity - line.Quantity;
            if (difference < 0 && batch.OnHand + difference < 0) { Error("Quantity", StockGone, true); continue; }

            var product = await db.Products.FirstAsync(p => p.Id == line.ProductId);
            if (edit.MergeIntoProductId is { } targetId && targetId != product.Id)
            {
                if (!product.IsProvisional) { Error("MergeIntoProductId", "فقط قلم موقت را می‌توان در قلم دیگر ادغام کرد."); continue; }

                var target = await db.Products.FirstOrDefaultAsync(p => p.Id == targetId);
                if (target is null) { Error("MergeIntoProductId", "قلم مقصد پیدا نشد."); continue; }
                if (!target.IsActive) { Error("MergeIntoProductId", "قلم مقصد غیرفعال است."); continue; }

                if (await MergeAsync(db, product, target) is { } message) { Error("MergeIntoProductId", message, true); continue; }
                product = target;
            }
            else if (edit.Product is { } p)
            {
                if (p.Version != product.Version) { Error("Product", "این قلم در این فاصله تغییر کرده است؛ دوباره بارگذاری کنید.", true); version = true; continue; }
                if (await ApplyProductAsync(db, product, p, locations, usable) is { } message) { Error("Product", message, message == DuplicateMessage); continue; }
            }

            var batchErrors = ApplyBatch(batch, edit, locations, usable);
            if (batchErrors is { } be) { Error(be.Field, be.Message); continue; }

            if (versionedBatches.Add(batch.Id)) db.ExpectVersion(batch, edit.BatchVersion);
            if (difference != 0)
            {
                ledger.Post(batch, StockMovementKind.Adjustment, difference, DateTime.UtcNow, AdjustNote);
                line.Quantity = edit.Quantity;
            }
        }

        if (errors.Count > 0) return ApiProblems.LineErrors(errors, conflict, version);
        return await SaveChangesAsync(db, receipt!);
    }

    private const string DuplicateMessage = "قلم دیگری با همین نام، برند و بسته‌بندی هست؛ آن را ادغام کنید.";

    // قلم موقت: همه‌ی بچ‌ها، برداشت‌ها، ردیف‌های ورود و بارکدها به قلم مقصد می‌روند و قلم موقت غیرفعال می‌شود.
    // بچ هم‌شماره در مقصد = خطا، چون ادغام موجودی دو بچ به معنی حرکت انتقال است که این برش ندارد.
    private static async Task<string?> MergeAsync(WarehouseDbContext db, Product from, Product to)
    {
        var batches = await db.Batches.Where(b => b.ProductId == from.Id).ToListAsync();
        var keys = batches.Select(b => b.BatchKey).ToList();
        var clash = await db.Batches.AsNoTracking().Where(b => b.ProductId == to.Id && keys.Contains(b.BatchKey)).Select(b => b.BatchNumber).FirstOrDefaultAsync();
        if (clash is not null) return $"بچ «{clash}» در قلم مقصد هم هست؛ ادغام ممکن نیست. ابتدا یکی از دو بچ را در انبارگردانی یا اصلاح خالی کنید.";

        foreach (var batch in batches) batch.ProductId = to.Id;
        foreach (var w in await db.Withdrawals.Where(w => w.ProductId == from.Id).ToListAsync()) w.ProductId = to.Id;
        foreach (var l in await db.ReceiptLines.Where(l => l.ProductId == from.Id).ToListAsync()) l.ProductId = to.Id;

        var barcodes = await db.ProductBarcodes.Where(b => b.ProductId == from.Id).ToListAsync();
        foreach (var barcode in barcodes) barcode.ProductId = to.Id;
        if (barcodes.Count > 0)
        {
            var codes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == to.Id).Select(b => b.Code).ToListAsync();
            to.SearchText = ProductEndpoints.BuildSearchText(to, codes.Concat(barcodes.Select(b => b.Code)));
        }

        from.IsActive = false;
        from.IsProvisional = false;
        return null;
    }

    private static async Task<string?> ApplyProductAsync(WarehouseDbContext db, Product product, ReviewProductEdit edit, List<StorageLocation> locations, HashSet<Guid> usable)
    {
        var save = new SaveProductRequest { Name = edit.Name, Brand = edit.Brand, PackDescription = edit.PackDescription };
        if (ProductRules.Validate(save) is { Count: > 0 } errors) return errors[0].Message;

        if (edit.LocationId is { } locationId && edit.LocationId != product.LocationId)
        {
            if (locations.All(l => l.Id != locationId)) return "محل پیدا نشد.";
            if (!usable.Contains(locationId)) return "این محل غیرفعال است.";
        }

        product.Name = TextNormalizer.Clean(edit.Name)!;
        product.NameKey = TextNormalizer.Key(product.Name)!;
        product.Brand = TextNormalizer.Clean(edit.Brand);
        product.BrandKey = TextNormalizer.Key(product.Brand) ?? "";
        product.PackDescription = TextNormalizer.Clean(edit.PackDescription);
        product.PackKey = TextNormalizer.Key(product.PackDescription) ?? "";
        product.LocationId = edit.LocationId;
        product.RequiresExpiry = edit.RequiresExpiry;
        product.IsProvisional = false;

        if (await db.Products.AnyAsync(x => x.NameKey == product.NameKey && x.BrandKey == product.BrandKey && x.PackKey == product.PackKey && x.Id != product.Id)) return DuplicateMessage;

        var codes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == product.Id).Select(b => b.Code).ToListAsync();
        product.SearchText = ProductEndpoints.BuildSearchText(product, codes);
        return null;
    }

    private static (string Field, string Message)? ApplyBatch(Batch batch, ReviewLineEdit edit, List<StorageLocation> locations, HashSet<Guid> usable)
    {
        if (edit.BatchLocationId is { } locationId && locationId != batch.LocationId)
        {
            if (locations.All(l => l.Id != locationId)) return ("BatchLocationId", "محل پیدا نشد.");
            if (!usable.Contains(locationId)) return ("BatchLocationId", "این محل غیرفعال است.");
        }

        if (!string.IsNullOrWhiteSpace(edit.Expiry))
        {
            var parsed = ExpiryDate.Parse(edit.Expiry);
            if (parsed.Error is { } error) return ("Expiry", error);

            batch.ExpiryDate = parsed.Value!.Value.Date;
            batch.ExpiryPrecision = parsed.Value.Value.Precision;
            batch.ExpiryCalendar = parsed.Value.Value.Calendar;
        }

        batch.LocationId = edit.BatchLocationId;
        return null;
    }

    private static async Task<IResult> ApproveAsync(Guid id, ReviewRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var (receipt, problem) = await LoadPendingAsync(db, id, request.Version);
        if (problem is not null) return problem;

        var lines = await db.ReceiptLines.Where(l => l.ReceiptId == id).OrderBy(l => l.Position).ToListAsync();
        var products = await db.Products.Where(p => lines.Select(l => l.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var batches = await db.Batches.AsNoTracking().Where(b => lines.Select(l => l.BatchId).Contains(b.Id)).ToDictionaryAsync(b => b.Id);

        var errors = new List<ValidationError>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (products[lines[i].ProductId].RequiresExpiry && batches[lines[i].BatchId].ExpiryDate is null)
                errors.Add(new ValidationError(BulkReceiptErrors.Key(i, "Expiry"), "تاریخ انقضا را وارد کنید."));
        }

        if (errors.Count > 0) return ApiProblems.LineErrors(errors, false, false);

        // تأیید یعنی انباردار قلم را دیده؛ دیگر «موقت» نیست.
        foreach (var product in products.Values.Where(p => p.IsProvisional)) product.IsProvisional = false;
        MarkReviewed(receipt!, ReceiptStatus.Approved, userId, null);
        return await SaveChangesAsync(db, receipt!);
    }

    private static async Task<IResult> RejectAsync(Guid id, ReviewRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var note = TextNormalizer.Clean(request.Note);
        if (note is null) return ApiProblems.Validation("Note", "برای رد کردن، یادداشت لازم است.");
        if (note.Length > Receipt.MaxNote) return ApiProblems.Validation("Note", $"یادداشت حداکثر {Receipt.MaxNote} نویسه است.");

        var (receipt, problem) = await LoadPendingAsync(db, id, request.Version);
        if (problem is not null) return problem;

        var lines = await db.ReceiptLines.Where(l => l.ReceiptId == id).OrderBy(l => l.Position).ToListAsync();
        var batchIds = lines.Select(l => l.BatchId).Distinct().ToList();
        var batches = await db.Batches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);
        foreach (var batch in batches.Values) db.ExpectVersion(batch, batch.Version);

        // موجودی بچ تا این لحظه (پس از ردیف‌های قبلی همین رد) باید به اندازه‌ی ورود باشد، وگرنه بخشی برداشته شده.
        foreach (var line in lines)
        {
            var batch = batches[line.BatchId];
            if (batch.OnHand < line.Quantity) return ApiProblems.Conflict($"{StockGone} (بچ {batch.BatchNumber})");

            ledger.Post(batch, StockMovementKind.Reversal, -line.Quantity, DateTime.UtcNow, note, reversesId: line.MovementId);
        }

        MarkReviewed(receipt!, ReceiptStatus.Rejected, userId, note);
        audit.Reason = note;
        return await SaveChangesAsync(db, receipt!);
    }

    private static void MarkReviewed(Receipt receipt, ReceiptStatus status, Guid reviewerId, string? note)
    {
        receipt.Status = status;
        receipt.ReviewedById = reviewerId;
        receipt.ReviewedAtUtc = DateTime.UtcNow;
        receipt.ReviewNote = note;
    }
}
