using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// قلم و بارکد. خواندن با stock.view، تغییر با products.manage.
// فهرست‌ها کوچک‌اند؛ فیلتر و ترتیب فارسی در حافظه انجام می‌شود، نه در SQLite.
public static class ProductEndpoints
{
    private const string DuplicateProduct = "قلمی با همین نام، برند و بسته‌بندی وجود دارد.";
    private const int ExpiringDays = 180; // برش ۵ با تنظیمات جایگزین می‌شود

    public static void MapProductEndpoints(this WebApplication app)
    {
        var read = app.MapGroup(ApiRoutes.Products).RequireAuthorization(Permissions.StockView);
        read.MapGet("", ListAsync);
        read.MapGet("by-code/{code}", ByCodeAsync);
        read.MapGet("{id:guid}", GetAsync);
        read.MapGet("{id:guid}/history", HistoryAsync);

        var manage = app.MapGroup(ApiRoutes.Products).RequireAuthorization(Permissions.ProductsManage);
        manage.MapPost("", CreateAsync);
        manage.MapPut("{id:guid}", UpdateAsync);
        manage.MapPost("{id:guid}/deactivate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetActiveAsync(id, r, false, db));
        manage.MapPost("{id:guid}/activate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetActiveAsync(id, r, true, db));
        manage.MapGet("{id:guid}/dependencies", DependenciesAsync);
        manage.MapDelete("{id:guid}", DeleteAsync);
        manage.MapPost("{id:guid}/barcodes", AddBarcodeAsync);
        manage.MapDelete("{id:guid}/barcodes/{barcodeId:guid}", RemoveBarcodeAsync);
    }

    // «امروز» به وقت محلی (LocalDay)، نه UTC.
    private static DateOnly Today() => DateOnly.FromDateTime((DateTime.UtcNow + LocalDay.OffsetFromUtc).Date);

    private static string BuildSearchText(Product p, IEnumerable<string> codes) =>
        TextNormalizer.SearchKey(string.Join(' ', new[] { p.Name, p.Brand, p.PackDescription }.Concat(codes).Where(s => !string.IsNullOrEmpty(s))))!;

    private sealed record Locations(IReadOnlyDictionary<Guid, string> Paths, List<StorageLocation> All);

    private static async Task<Locations> LoadLocationsAsync(WarehouseDbContext db)
    {
        var all = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(all.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        return new Locations(paths, all);
    }

    private static string? PathOf(Locations l, Guid? id) => id is { } g && l.Paths.TryGetValue(g, out var p) ? p : null;

    private static ProductListItem ToListItem(Product p, List<Batch> batches, Locations locations, DateOnly today)
    {
        var onHand = batches.Sum(b => b.OnHand);
        var usable = batches.Where(b => b.ExpiryDate is null || b.ExpiryDate >= today).Sum(b => b.OnHand);
        var nearest = batches.Where(b => b.OnHand > 0 && b.ExpiryDate is not null).Select(b => b.ExpiryDate).Min();
        return new ProductListItem(p.Id, p.Name, p.Brand, p.PackDescription, PathOf(locations, p.LocationId),
            onHand, usable, p.MinStock, nearest, p.IsActive, p.Version);
    }

    private static async Task<ProductDetail> ToDetailAsync(WarehouseDbContext db, Product p)
    {
        var locations = await LoadLocationsAsync(db);
        var today = Today();
        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == p.Id).ToListAsync();
        var barcodes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == p.Id).ToListAsync();

        return new ProductDetail
        {
            Summary = ToListItem(p, batches, locations, today),
            RequiresExpiry = p.RequiresExpiry,
            AllowExpiredWithdrawal = p.AllowExpiredWithdrawal,
            LocationId = p.LocationId,
            Batches = batches
                .OrderBy(b => b.ExpiryDate is null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.BatchNumber, StringComparer.Ordinal)
                .Select(b => new BatchItem(b.Id, b.BatchNumber, b.ExpiryDate, b.ExpiryPrecision, b.ExpiryCalendar,
                    b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null,
                    PathOf(locations, b.LocationId), b.OnHand, b.ExpiryDate is { } e && e < today, b.Version))
                .ToList(),
            Barcodes = barcodes.OrderBy(b => b.Code, StringComparer.Ordinal).Select(b => new BarcodeItem(b.Id, b.Code)).ToList()
        };
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db, string? q, Guid? locationId, string? filter, bool includeInactive = false)
    {
        var products = await db.Products.AsNoTracking().Where(p => includeInactive || p.IsActive).ToListAsync();

        if (TextNormalizer.SearchKey(q) is { } key) products = products.Where(p => p.SearchText.Contains(key)).ToList();

        var locations = await LoadLocationsAsync(db);
        if (locationId is { } root)
        {
            // خود محل و همه‌ی زیرشاخه‌هایش
            var ids = new HashSet<Guid> { root };
            for (var grew = true; grew;)
            {
                grew = false;
                foreach (var l in locations.All)
                {
                    if (l.ParentId is { } parent && ids.Contains(parent) && ids.Add(l.Id)) grew = true;
                }
            }

            products = products.Where(p => p.LocationId is { } l && ids.Contains(l)).ToList();
        }

        var byProduct = (await db.Batches.AsNoTracking().ToListAsync()).ToLookup(b => b.ProductId);
        var today = Today();
        var limit = today.AddDays(ExpiringDays);
        var items = products
            .Select(p => (Product: p, Batches: byProduct[p.Id].ToList()))
            .Where(x => filter switch
            {
                "zero" => x.Batches.Sum(b => b.OnHand) == 0,
                "belowMin" => x.Product.MinStock is { } min
                    && x.Batches.Where(b => b.ExpiryDate is null || b.ExpiryDate >= today).Sum(b => b.OnHand) < min,
                "expiring" => x.Batches.Any(b => b.OnHand > 0 && b.ExpiryDate is { } d && d <= limit),
                _ => true
            })
            .OrderBy(x => x.Product.Name, StringComparer.Ordinal)
            .ThenBy(x => x.Product.Brand ?? "", StringComparer.Ordinal)
            .ThenBy(x => x.Product.PackDescription ?? "", StringComparer.Ordinal)
            .Select(x => ToListItem(x.Product, x.Batches, locations, today))
            .ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult> ByCodeAsync(string code, WarehouseDbContext db)
    {
        var key = TextNormalizer.ScanKey(code);
        if (key is null) return ApiProblems.NotFound();

        var productId = await db.ProductBarcodes.AsNoTracking().Where(b => b.CodeKey == key).Select(b => (Guid?)b.ProductId).FirstOrDefaultAsync();
        if (productId is null) return ApiProblems.NotFound();

        var product = await db.Products.AsNoTracking().FirstAsync(p => p.Id == productId);
        return Results.Ok(await ToDetailAsync(db, product));
    }

    private static async Task<IResult> GetAsync(Guid id, WarehouseDbContext db)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return product is null ? ApiProblems.NotFound() : Results.Ok(await ToDetailAsync(db, product));
    }

    private static async Task<IResult> HistoryAsync(Guid id, WarehouseDbContext db)
    {
        if (!await db.Products.AnyAsync(p => p.Id == id)) return ApiProblems.NotFound();

        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == id).Select(b => new { b.Id, b.BatchNumber }).ToListAsync();
        var batchIds = batches.Select(b => b.Id).ToList();
        var numbers = batches.ToDictionary(b => b.Id, b => b.BatchNumber);

        var movements = await db.StockMovements.AsNoTracking().Where(m => batchIds.Contains(m.BatchId)).ToListAsync();
        var userIds = movements.Where(m => m.UserId != null).Select(m => m.UserId!.Value).Distinct().ToList();
        var users = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).Select(u => new { u.Id, u.UserName }).ToListAsync();
        var names = users.ToDictionary(u => u.Id, u => u.UserName ?? "");

        var audit = await db.AuditEntries.AsNoTracking().Where(a => a.SubjectId == id).ToListAsync();

        return Results.Ok(new ProductHistory
        {
            Movements = movements.OrderByDescending(m => m.AtUtc)
                .Select(m => new MovementItem(m.Id, m.BatchId, numbers[m.BatchId], m.Kind, m.Quantity, m.AtUtc, m.OccurredAtUtc,
                    m.UserId is { } u && names.TryGetValue(u, out var n) ? n : "سیستم", m.Note))
                .ToList(),
            Changes = audit.OrderByDescending(a => a.AtUtc)
                .Select(a => new AuditEntryItem
                {
                    Id = a.Id, ChangeSetId = a.ChangeSetId, AtUtc = a.AtUtc, UserName = a.UserName, EntityType = a.EntityType,
                    EntityLabel = a.EntityLabel, Action = a.Action, Field = a.Field, OldValue = a.OldValue, NewValue = a.NewValue,
                    Reason = a.Reason
                })
                .ToList()
        });
    }

    // محل باید موجود و (همراه نیاکانش) فعال باشد؛ نگه داشتن محل غیرفعال فعلی در ویرایش ایرادی ندارد.
    private static async Task<IResult?> ValidateAsync(WarehouseDbContext db, SaveProductRequest request, Guid? unchangedLocation = null)
    {
        var errors = ProductRules.Validate(request);
        if (errors.Count > 0) return ApiProblems.Validation(errors);

        if (request.LocationId is { } id)
        {
            var all = await db.StorageLocations.AsNoTracking().ToListAsync();
            if (all.All(l => l.Id != id)) return ApiProblems.Validation("LocationId", "محل پیدا نشد.");

            var usable = LocationTree.EffectivelyActive(all.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
            if (!usable.Contains(id) && id != unchangedLocation) return ApiProblems.Validation("LocationId", "این محل غیرفعال است.");
        }

        return null;
    }

    private static void Apply(Product p, SaveProductRequest r)
    {
        p.Name = TextNormalizer.Clean(r.Name)!;
        p.NameKey = TextNormalizer.Key(p.Name)!;
        p.Brand = TextNormalizer.Clean(r.Brand);
        p.BrandKey = TextNormalizer.Key(p.Brand) ?? "";
        p.PackDescription = TextNormalizer.Clean(r.PackDescription);
        p.PackKey = TextNormalizer.Key(p.PackDescription) ?? "";
        p.LocationId = r.LocationId;
        p.MinStock = r.MinStock;
        p.RequiresExpiry = r.RequiresExpiry;
        p.AllowExpiredWithdrawal = r.AllowExpiredWithdrawal;
    }

    private static async Task<IResult> SaveAsync(WarehouseDbContext db, Product product, string conflictMessage = DuplicateProduct)
    {
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(await ToDetailAsync(db, product));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict(conflictMessage);
        }
        catch (DbUpdateException)
        {
            // محل بین اعتبارسنجی و ذخیره حذف شد؛ کلید خارجی جلوی ذخیره را گرفت.
            return ApiProblems.Validation("LocationId", "محل پیدا نشد.");
        }
    }

    private static Task<bool> DuplicateExistsAsync(WarehouseDbContext db, Product p, Guid? exceptId) =>
        db.Products.AnyAsync(x => x.NameKey == p.NameKey && x.BrandKey == p.BrandKey && x.PackKey == p.PackKey && x.Id != exceptId);

    private static async Task<IResult> CreateAsync(SaveProductRequest request, WarehouseDbContext db)
    {
        if (await ValidateAsync(db, request) is { } invalid) return invalid;

        var product = new Product { Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };
        Apply(product, request);
        if (await DuplicateExistsAsync(db, product, null)) return ApiProblems.Conflict(DuplicateProduct);

        product.SearchText = BuildSearchText(product, []);
        db.Products.Add(product);
        return await SaveAsync(db, product);
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveProductRequest request, WarehouseDbContext db)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return ApiProblems.NotFound();

        if (await ValidateAsync(db, request, product.LocationId) is { } invalid) return invalid;

        Apply(product, request);
        if (await DuplicateExistsAsync(db, product, id)) return ApiProblems.Conflict(DuplicateProduct);

        var codes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == id).Select(b => b.Code).ToListAsync();
        product.SearchText = BuildSearchText(product, codes);
        db.ExpectVersion(product, request.Version);
        return await SaveAsync(db, product);
    }

    private static async Task<IResult> SetActiveAsync(Guid id, VersionRequest request, bool active, WarehouseDbContext db)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return ApiProblems.NotFound();

        product.IsActive = active;
        db.ExpectVersion(product, request.Version);
        return await SaveAsync(db, product);
    }

    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id)
    {
        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == id).Select(b => b.BatchNumber).ToListAsync();
        var items = batches.Order(StringComparer.Ordinal).Select(n => new DependencyItem("بچ", n, id)).ToList();
        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.Products.AnyAsync(p => p.Id == id)
            ? Results.Ok(await FindDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> DeleteAsync(Guid id, WarehouseDbContext db)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return ApiProblems.NotFound();

        const string blocked = "این قلم بچ دارد و حذف نمی‌شود؛ به‌جای آن غیرفعالش کنید.";
        if (!(await FindDependenciesAsync(db, id)).CanDelete) return ApiProblems.Conflict(blocked);

        // بارکدها در همان SaveChanges حذف می‌شوند تا رهگیر ثبتشان کند.
        db.ProductBarcodes.RemoveRange(await db.ProductBarcodes.Where(b => b.ProductId == id).ToListAsync());
        db.Products.Remove(product);
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(new DependencyList());
        }
        catch (DbUpdateException)
        {
            // بچی که هم‌زمان ساخته شد؛ کلید خارجی Restrict جلوی حذف را گرفت.
            return ApiProblems.Conflict(blocked);
        }
    }

    private static async Task<IResult> AddBarcodeAsync(Guid id, AddBarcodeRequest request, WarehouseDbContext db)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return ApiProblems.NotFound();

        var code = TextNormalizer.Clean(request.Code);
        var key = TextNormalizer.ScanKey(code);
        if (code is null || key is null) return ApiProblems.Validation("Code", "بارکد را وارد کنید.");
        if (code.Length > ProductRules.MaxCode) return ApiProblems.Validation("Code", $"بارکد حداکثر {ProductRules.MaxCode} نویسه است.");

        var ownerName = await OwnerNameAsync(db, key);
        if (ownerName is not null) return ApiProblems.Conflict($"این بارکد مال «{ownerName}» است.");

        var codes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == id).Select(b => b.Code).ToListAsync();
        db.ProductBarcodes.Add(new ProductBarcode { Id = Guid.NewGuid(), ProductId = id, Code = code, CodeKey = key });
        product.SearchText = BuildSearchText(product, codes.Append(code));
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(await ToDetailAsync(db, product));
        }
        catch (DbUpdateConcurrencyException)
        {
            // درخواست هم‌زمان دیگری SearchText و Version قلم را عوض کرد.
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            // دو درخواست هم‌زمان با یک بارکد
            db.ChangeTracker.Clear();
            return ApiProblems.Conflict($"این بارکد مال «{await OwnerNameAsync(db, key) ?? "قلم دیگر"}» است.");
        }
        catch (DbUpdateException)
        {
            return ApiProblems.NotFound();
        }
    }

    private static async Task<string?> OwnerNameAsync(WarehouseDbContext db, string key)
    {
        var owner = await db.ProductBarcodes.AsNoTracking().Where(b => b.CodeKey == key).Select(b => (Guid?)b.ProductId).FirstOrDefaultAsync();
        return owner is { } pid ? await db.Products.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync() : null;
    }

    private static async Task<IResult> RemoveBarcodeAsync(Guid id, Guid barcodeId, WarehouseDbContext db)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id);
        var barcode = await db.ProductBarcodes.FirstOrDefaultAsync(b => b.Id == barcodeId && b.ProductId == id);
        if (product is null || barcode is null) return ApiProblems.NotFound();

        var codes = await db.ProductBarcodes.AsNoTracking().Where(b => b.ProductId == id && b.Id != barcodeId).Select(b => b.Code).ToListAsync();
        db.ProductBarcodes.Remove(barcode);
        product.SearchText = BuildSearchText(product, codes);
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(await ToDetailAsync(db, product));
        }
        catch (DbUpdateConcurrencyException)
        {
            // ۴۰۴ فقط وقتی خود بارکد رفته است؛ وگرنه تغییر هم‌زمان قلم (SearchText) است.
            db.ChangeTracker.Clear();
            return await db.ProductBarcodes.AnyAsync(b => b.Id == barcodeId && b.ProductId == id)
                ? ApiProblems.VersionConflict()
                : ApiProblems.NotFound();
        }
    }
}
