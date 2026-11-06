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

// ورود جنس ساده (بخش ۱۵.۲ spec): موجودی همان لحظه زیاد می‌شود؛ بررسی انباردار در ReceiptReviewEndpoints.
// حرکت Receipt و OnHand از StockLedger می‌آید و همه‌ی ردیف‌ها در یک SaveChanges هستند (همه یا هیچ).
public static class QuickReceiptEndpoints
{
    private const int MaxListTake = 200;
    private const string Race = "هم‌زمان با ثبت دیگری انجام شد؛ دوباره تلاش کنید.";

    public static void MapQuickReceiptEndpoints(this WebApplication app)
    {
        var submit = app.MapGroup(ApiRoutes.Receipts).RequireAuthorization(Permissions.ReceiptSubmit);
        submit.MapPost("quick", SubmitAsync);
        submit.MapGet("mine", MineAsync);

        // دیدن یک ورود: ثبت‌کننده یا دارنده‌ی stock.receive.
        app.MapGet($"{ApiRoutes.Receipts}/{{id:guid}}", GetAsync).RequireAuthorization();
    }

    private static async Task<IResult> SubmitAsync(QuickReceiptRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        if (request.Lines is not { Count: > 0 } lines) return ApiProblems.Validation("Lines", "هیچ ردیفی برای ثبت نیست.");
        if (lines.Count > QuickReceiptRequest.MaxLines) return ApiProblems.Validation("Lines", $"حداکثر {QuickReceiptRequest.MaxLines} ردیف در یک ثبت ممکن است.");

        var note = TextNormalizer.Clean(request.Note);
        if (note is { Length: > Receipt.MaxNote }) return ApiProblems.Validation("Note", $"یادداشت حداکثر {Receipt.MaxNote} نویسه است.");

        var now = DateTime.UtcNow;
        var receipt = new Receipt { Id = Guid.NewGuid(), UserId = userId, AtUtc = now, Note = note };
        var provisional = new Dictionary<string, Product>();
        var batchesSeen = new Dictionary<(Guid Product, string Key), Batch>();
        var errors = new List<ValidationError>();
        var plan = new List<(Product Product, Batch Batch, int Quantity)>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            void Error(string field, string message) => errors.Add(new ValidationError(BulkReceiptErrors.Key(i, field), message));

            var batchNumber = TextNormalizer.Clean(line.BatchNumber);
            var batchKey = TextNormalizer.ScanKey(batchNumber);
            if (batchNumber is null || batchKey is null) { Error("BatchNumber", "بچ نامبر را وارد کنید."); continue; }
            if (batchNumber.Length > ProductRules.MaxBatch) { Error("BatchNumber", $"بچ نامبر حداکثر {ProductRules.MaxBatch} نویسه است."); continue; }
            if (line.Quantity <= 0) { Error("Quantity", "تعداد باید بیشتر از صفر باشد."); continue; }

            var newName = TextNormalizer.Clean(line.NewName);
            if (line.ProductId is null == (newName is null)) { Error("ProductId", "یک قلم انتخاب کنید یا نام قلم تازه را بنویسید."); continue; }

            Product? product;
            if (line.ProductId is { } productId)
            {
                product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId);
                if (product is null) { Error("ProductId", "این قلم پیدا نشد."); continue; }
                if (!product.IsActive) { Error("ProductId", "این قلم غیرفعال است."); continue; }
            }
            else
            {
                if (newName!.Length > ProductRules.MaxName) { Error("NewName", $"نام قلم حداکثر {ProductRules.MaxName} نویسه است."); continue; }

                var nameKey = TextNormalizer.Key(newName)!;
                if (!provisional.TryGetValue(nameKey, out product))
                {
                    // همان نام بدون برند و بسته‌بندی از قبل هست: همان را می‌گیریم، نه قلم موقتِ تکراری.
                    product = await db.Products.FirstOrDefaultAsync(p => p.NameKey == nameKey && p.BrandKey == "" && p.PackKey == "");
                    if (product is { IsActive: false }) { Error("NewName", "قلمی با این نام هست ولی غیرفعال است."); continue; }

                    if (product is null)
                    {
                        product = new Product
                        {
                            Id = Guid.NewGuid(),
                            Name = newName,
                            NameKey = nameKey,
                            BrandKey = "",
                            PackKey = "",
                            RequiresExpiry = false, // تا انباردار تکمیل کند
                            IsProvisional = true,
                            CreatedAtUtc = now
                        };
                        product.SearchText = ProductEndpoints.BuildSearchText(product, []);
                        db.Products.Add(product);
                    }

                    provisional[nameKey] = product;
                }
            }

            // بچ هم‌کلید موجود: به همان اضافه می‌شود؛ وگرنه بچ تازه بدون تاریخ و بدون محل (محل قلم).
            if (!batchesSeen.TryGetValue((product.Id, batchKey), out var batch))
            {
                batch = db.Entry(product).State == EntityState.Added
                    ? null
                    : await db.Batches.FirstOrDefaultAsync(b => b.ProductId == product.Id && b.BatchKey == batchKey);
                if (batch is not null) db.ExpectVersion(batch, batch.Version);
                else
                {
                    batch = new Batch { Id = Guid.NewGuid(), ProductId = product.Id, BatchNumber = batchNumber, BatchKey = batchKey, CreatedAtUtc = now };
                    db.Batches.Add(batch);
                }

                batchesSeen[(product.Id, batchKey)] = batch;
            }

            plan.Add((product, batch, line.Quantity));
        }

        if (errors.Count > 0) return ApiProblems.LineErrors(errors, false, false);

        db.Receipts.Add(receipt);
        for (var i = 0; i < plan.Count; i++)
        {
            var (product, batch, quantity) = plan[i];
            var movement = ledger.Post(batch, StockMovementKind.Receipt, quantity, now, "ورود ساده توسط پرسنل");
            db.ReceiptLines.Add(new ReceiptLine
            {
                Id = Guid.NewGuid(), ReceiptId = receipt.Id, Position = i, ProductId = product.Id,
                BatchId = batch.Id, MovementId = movement.Id, Quantity = quantity
            });
        }

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
            return ApiProblems.Conflict(Race);
        }

        return Results.Ok((await ToItemsAsync(db, [receipt])).Single());
    }

    private static async Task<IResult> MineAsync(WarehouseDbContext db, AuditContext audit, int take = 50)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        take = Math.Clamp(take, 1, MaxListTake);
        var page = await db.Receipts.AsNoTracking().Where(r => r.UserId == userId)
            .OrderByDescending(r => r.AtUtc).ThenByDescending(r => r.Id).Take(take).ToListAsync();
        return Results.Ok(await ToItemsAsync(db, page));
    }

    private static async Task<IResult> GetAsync(Guid id, WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        var receipt = await db.Receipts.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (receipt is null) return ApiProblems.NotFound();

        if (receipt.UserId != audit.UserId && !await permissions.HasAsync(audit.UserId, Permissions.StockReceive)) return ApiProblems.NotFound();

        return Results.Ok((await ToItemsAsync(db, [receipt])).Single());
    }

    // نگاشت مشترک با بررسی انباردار. ردیف‌ها به ترتیب Position.
    internal static async Task<List<ReceiptItem>> ToItemsAsync(WarehouseDbContext db, IReadOnlyList<Receipt> receipts)
    {
        if (receipts.Count == 0) return [];

        var ids = receipts.Select(r => r.Id).ToList();
        var lines = await db.ReceiptLines.AsNoTracking().Where(l => ids.Contains(l.ReceiptId)).ToListAsync();
        var productIds = lines.Select(l => l.ProductId).Distinct().ToList();
        var batchIds = lines.Select(l => l.BatchId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        string? PathOf(Guid? id) => id is { } g && paths.TryGetValue(g, out var p) ? p : null;

        var userIds = receipts.Select(r => r.UserId).Concat(receipts.Where(r => r.ReviewedById != null).Select(r => r.ReviewedById!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");
        string NameOf(Guid id) => names.TryGetValue(id, out var n) ? n : "—";

        var byReceipt = lines.ToLookup(l => l.ReceiptId);
        return receipts.Select(r =>
        {
            var items = byReceipt[r.Id].OrderBy(l => l.Position).Select(l =>
            {
                var p = products[l.ProductId];
                var b = batches[l.BatchId];
                var expiry = b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null;
                return new ReceiptLineItem(l.Id, p.Id, p.Name, p.Brand, p.PackDescription, p.IsProvisional, p.RequiresExpiry, p.LocationId, p.Version,
                    b.Id, b.BatchNumber, expiry, b.LocationId, PathOf(b.LocationId) ?? PathOf(p.LocationId), l.Quantity, b.OnHand, b.Version, l.Version);
            }).ToList();
            return new ReceiptItem(r.Id, NameOf(r.UserId), r.AtUtc, r.Note, r.Status, r.ReviewNote,
                r.ReviewedById is { } rid ? NameOf(rid) : null, r.ReviewedAtUtc, items, r.Version);
        }).ToList();
    }
}
