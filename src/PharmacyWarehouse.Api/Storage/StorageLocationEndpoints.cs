using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Storage;

// محل‌های درختی انبار. خواندن با stock.view، تغییر با products.manage.
// مسیر و حلقه با LocationTree در Domain حساب می‌شوند؛ درخت کوچک است و همه‌اش خوانده می‌شود.
public static class StorageLocationEndpoints
{
    private const string Duplicate = "محلی با این نام در همین سطح وجود دارد.";

    public static void MapStorageLocationEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Locations, ListAsync).RequireAuthorization(Permissions.StockView);

        var manage = app.MapGroup(ApiRoutes.Locations).RequireAuthorization(Permissions.ProductsManage);
        manage.MapPost("", CreateAsync);
        manage.MapPut("{id:guid}", UpdateAsync);
        manage.MapPost("{id:guid}/deactivate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetActiveAsync(id, r, false, db));
        manage.MapPost("{id:guid}/activate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetActiveAsync(id, r, true, db));
        manage.MapGet("{id:guid}/dependencies", DependenciesAsync);
        manage.MapDelete("{id:guid}", DeleteAsync);
    }

    private static async Task<List<LocationNode>> NodesAsync(WarehouseDbContext db) =>
        await db.StorageLocations.AsNoTracking()
            .Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder))
            .ToListAsync();

    // مسیر از روی درخت ذخیره‌شده به‌اضافه‌ی همین گره (که شاید هنوز ذخیره نشده یا تازه تغییر کرده).
    // IsUsable به فعال بودن نیاکان هم بستگی دارد، پس وضعیت فعال بقیه‌ی گره‌ها هم خوانده می‌شود.
    private static async Task<StorageLocationItem> ToItemAsync(WarehouseDbContext db, StorageLocation location)
    {
        var stored = await db.StorageLocations.AsNoTracking().Where(l => l.Id != location.Id).ToListAsync();
        var entries = stored.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)).ToList();
        entries.Add((new LocationNode(location.Id, location.ParentId, location.Name, location.SortOrder), location.IsActive));

        var nodes = entries.Select(e => e.Item1).ToList();
        var path = LocationTree.Paths(nodes)[location.Id];
        var usable = LocationTree.EffectivelyActive(entries).Contains(location.Id);
        return new StorageLocationItem(location.Id, location.ParentId, location.Name, path, location.SortOrder, location.IsActive, location.Version, usable);
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var nodes = locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)).ToList();
        var paths = LocationTree.Paths(nodes);
        var usable = LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
        var byId = locations.ToDictionary(l => l.Id);

        // ترتیب درختی در حافظه، نه در SQLite (مقایسه‌ی متن فارسی در SQLite ممنوع است).
        var items = LocationTree.Ordered(nodes)
            .Select(o => byId[o.Node.Id])
            .Select(l => new StorageLocationItem(l.Id, l.ParentId, l.Name, paths[l.Id], l.SortOrder, l.IsActive, l.Version, usable.Contains(l.Id)))
            .ToList();
        return Results.Ok(items);
    }

    // unchangedParent: در ویرایش، پدر فعلی؛ نگه داشتن پدرِ غیرفعال ایرادی ندارد، فقط رفتن زیر آن.
    private static async Task<IResult?> ValidateAsync(WarehouseDbContext db, string? name, Guid? parentId, Guid? unchangedParent = null)
    {
        if (name is null) return ApiProblems.Validation("Name", "نام محل را وارد کنید.");
        if (name.Length > LocationTree.MaxName) return ApiProblems.Validation("Name", $"نام محل حداکثر {LocationTree.MaxName} نویسه است.");
        if (parentId is { } p)
        {
            var all = await db.StorageLocations.AsNoTracking().ToListAsync();
            if (all.All(l => l.Id != p)) return ApiProblems.Validation("ParentId", "محل بالاتر پیدا نشد.");

            var usable = LocationTree.EffectivelyActive(all.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
            if (!usable.Contains(p) && p != unchangedParent) return ApiProblems.Validation("ParentId", "محل بالاتر غیرفعال است.");
        }

        return null;
    }

    private static async Task<IResult> SaveAsync(WarehouseDbContext db, StorageLocation location)
    {
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(await ToItemAsync(db, location));
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict(Duplicate);
        }
        catch (DbUpdateException)
        {
            // محل بالا بین اعتبارسنجی و ذخیره حذف شد؛ کلید خارجی جلوی ذخیره را گرفت.
            return ApiProblems.Validation("ParentId", "محل بالاتر پیدا نشد.");
        }
    }

    private static async Task<IResult> CreateAsync(SaveStorageLocationRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (await ValidateAsync(db, name, request.ParentId) is { } invalid) return invalid;

        var key = TextNormalizer.Key(name)!;
        var parentKey = StorageLocation.KeyOf(request.ParentId);
        if (await db.StorageLocations.AnyAsync(l => l.ParentKey == parentKey && l.NameKey == key)) return ApiProblems.Conflict(Duplicate);

        var location = new StorageLocation
        {
            Id = Guid.NewGuid(),
            ParentId = request.ParentId,
            ParentKey = parentKey,
            Name = name!,
            NameKey = key,
            SortOrder = request.SortOrder
        };
        db.StorageLocations.Add(location);
        return await SaveAsync(db, location);
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveStorageLocationRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        var currentParent = await db.StorageLocations.AsNoTracking().Where(l => l.Id == id).Select(l => l.ParentId).FirstOrDefaultAsync();
        if (await ValidateAsync(db, name, request.ParentId, currentParent) is { } invalid) return invalid;

        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null) return ApiProblems.NotFound();

        if (LocationTree.WouldCreateCycle(await NodesAsync(db), id, request.ParentId))
        {
            return ApiProblems.Conflict("یک محل نمی‌تواند زیر خودش یا زیرشاخه‌های خودش برود.");
        }

        var key = TextNormalizer.Key(name)!;
        var parentKey = StorageLocation.KeyOf(request.ParentId);
        if (await db.StorageLocations.AnyAsync(l => l.ParentKey == parentKey && l.NameKey == key && l.Id != id))
        {
            return ApiProblems.Conflict(Duplicate);
        }

        location.Name = name!;
        location.NameKey = key;
        location.ParentId = request.ParentId;
        location.ParentKey = parentKey;
        location.SortOrder = request.SortOrder;
        db.ExpectVersion(location, request.Version);
        return await SaveAsync(db, location);
    }

    private static async Task<IResult> SetActiveAsync(Guid id, VersionRequest request, bool active, WarehouseDbContext db)
    {
        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null) return ApiProblems.NotFound();

        location.IsActive = active;
        db.ExpectVersion(location, request.Version);
        return await SaveAsync(db, location);
    }

    // برش ۱ اقلام و بچ‌های این محل را به فهرست اضافه می‌کند.
    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id)
    {
        var children = await db.StorageLocations.AsNoTracking()
            .Where(l => l.ParentId == id)
            .Select(l => l.Name)
            .ToListAsync();

        var items = children.Order(StringComparer.Ordinal).Select(n => new DependencyItem("زیرشاخه", n)).ToList();

        var products = await db.Products.AsNoTracking()
            .Where(p => p.LocationId == id)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();
        items.AddRange(products.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new DependencyItem("قلم", p.Name, p.Id)));

        var batches = await db.Batches.AsNoTracking()
            .Where(b => b.LocationId == id)
            .Join(db.Products, b => b.ProductId, p => p.Id, (b, p) => new { p.Id, p.Name, b.BatchNumber })
            .ToListAsync();
        items.AddRange(batches
            .OrderBy(b => b.Name, StringComparer.Ordinal).ThenBy(b => b.BatchNumber, StringComparer.Ordinal)
            .Select(b => new DependencyItem("بچ", $"{b.Name} — {b.BatchNumber}", b.Id)));

        // نوبت انبارگردانی حذف ندارد، پس محلش هم حذف نمی‌شود؛ غیرفعال کردن همیشه ممکن است.
        var stocktakes = await db.Stocktakes.AsNoTracking().Where(s => s.LocationId == id).Select(s => s.StartedAtUtc).ToListAsync();
        items.AddRange(stocktakes.Order().Select(at => new DependencyItem("انبارگردانی", PharmacyWarehouse.Domain.Time.JalaliDate.Format(at))));

        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.StorageLocations.AnyAsync(l => l.Id == id)
            ? Results.Ok(await FindDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> DeleteAsync(Guid id, WarehouseDbContext db)
    {
        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null) return ApiProblems.NotFound();

        var dependencies = await FindDependenciesAsync(db, id);
        if (!dependencies.CanDelete) return ApiProblems.Conflict("این محل زیرشاخه، قلم، بچ یا انبارگردانی دارد و حذف نمی‌شود.");

        db.StorageLocations.Remove(location);
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(new DependencyList());
        }
        catch (DbUpdateException)
        {
            // زیرشاخه‌ای که هم‌زمان ساخته شد؛ کلید خارجی Restrict جلوی حذف را گرفت.
            return ApiProblems.Conflict("این محل زیرشاخه، قلم، بچ یا انبارگردانی دارد و حذف نمی‌شود.");
        }
    }
}
