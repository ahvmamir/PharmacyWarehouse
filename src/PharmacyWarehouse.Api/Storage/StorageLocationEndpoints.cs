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
    private static async Task<StorageLocationItem> ToItemAsync(WarehouseDbContext db, StorageLocation location)
    {
        var nodes = (await NodesAsync(db)).Where(n => n.Id != location.Id).ToList();
        nodes.Add(new LocationNode(location.Id, location.ParentId, location.Name, location.SortOrder));
        var path = LocationTree.Paths(nodes)[location.Id];
        return new StorageLocationItem(location.Id, location.ParentId, location.Name, path, location.SortOrder, location.IsActive, location.Version);
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));

        // مرتب‌سازی مسیر در حافظه، نه در SQLite (مقایسه‌ی متن فارسی در SQLite ممنوع است).
        var items = locations
            .Select(l => new StorageLocationItem(l.Id, l.ParentId, l.Name, paths[l.Id], l.SortOrder, l.IsActive, l.Version))
            .OrderBy(i => i.Path, StringComparer.Ordinal)
            .ToList();
        return Results.Ok(items);
    }

    private static async Task<IResult?> ValidateAsync(WarehouseDbContext db, string? name, Guid? parentId)
    {
        if (name is null) return ApiProblems.Validation("Name", "نام محل را وارد کنید.");
        if (name.Length > LocationTree.MaxName) return ApiProblems.Validation("Name", $"نام محل حداکثر {LocationTree.MaxName} نویسه است.");
        if (parentId is { } p && !await db.StorageLocations.AnyAsync(l => l.Id == p))
        {
            return ApiProblems.Validation("ParentId", "محل بالاتر پیدا نشد.");
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
    }

    private static string KeyOf(Guid? parentId) => parentId?.ToString() ?? "";

    private static async Task<IResult> CreateAsync(SaveStorageLocationRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (await ValidateAsync(db, name, request.ParentId) is { } invalid) return invalid;

        var key = TextNormalizer.Key(name)!;
        var parentKey = KeyOf(request.ParentId);
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
        if (await ValidateAsync(db, name, request.ParentId) is { } invalid) return invalid;

        var location = await db.StorageLocations.FirstOrDefaultAsync(l => l.Id == id);
        if (location is null) return ApiProblems.NotFound();

        if (LocationTree.WouldCreateCycle(await NodesAsync(db), id, request.ParentId))
        {
            return ApiProblems.Conflict("یک محل نمی‌تواند زیر خودش یا زیرشاخه‌های خودش برود.");
        }

        var key = TextNormalizer.Key(name)!;
        var parentKey = KeyOf(request.ParentId);
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
        if (!dependencies.CanDelete) return ApiProblems.Conflict("این محل زیرشاخه دارد و حذف نمی‌شود.");

        db.StorageLocations.Remove(location);
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(new DependencyList());
        }
        catch (DbUpdateException)
        {
            // زیرشاخه‌ای که هم‌زمان ساخته شد؛ کلید خارجی Restrict جلوی حذف را گرفت.
            return ApiProblems.Conflict("این محل زیرشاخه دارد و حذف نمی‌شود.");
        }
    }
}
