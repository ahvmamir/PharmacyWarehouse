using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Stock;

// دسته‌ی قلم (بخش ۱۵.۳ spec). خواندن با هر مجوز یافتن قلم (فیلتر اقلام و فرم‌ها)؛ تغییر با products.manage.
// حذف فقط بدون قلم وابسته (۴۰۹)؛ غیرفعال کردن همیشه ممکن است و دسته را از فرم‌ها برمی‌دارد.
public static class CategoryEndpoints
{
    private const string Duplicate = "دسته‌ای با این نام وجود دارد.";
    private const string InUse = "این دسته قلم یا انبارگردانی دارد و حذف نمی‌شود؛ می‌توانید غیرفعالش کنید.";

    public static void MapCategoryEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Categories, ListAsync).RequireAuthorization(Permissions.ProductLookup);

        var manage = app.MapGroup(ApiRoutes.Categories).RequireAuthorization(Permissions.ProductsManage);
        manage.MapPost("", CreateAsync);
        manage.MapPut("{id:guid}", UpdateAsync);
        manage.MapGet("{id:guid}/dependencies", DependenciesAsync);
        manage.MapDelete("{id:guid}", DeleteAsync);

        app.MapPost(ApiRoutes.ProductsBulkCategory, BulkAssignAsync).RequireAuthorization(Permissions.ProductsManage);
    }

    private static async Task<IResult> BulkAssignAsync(BulkCategoryRequest request, WarehouseDbContext db)
    {
        if (request.Items is null || request.Items.Count == 0) return ApiProblems.Validation("Items", "حداقل یک قلم انتخاب کنید.");
        if (request.Items.Count > BulkCategoryRequest.MaxItems) return ApiProblems.Validation("Items", $"حداکثر {BulkCategoryRequest.MaxItems} قلم در هر بار.");
        if (request.Items.Select(t => t.Id).Distinct().Count() != request.Items.Count) return ApiProblems.Validation("Items", "یک قلم فقط یک بار می‌آید.");

        if (request.CategoryId is { } categoryId)
        {
            var category = await db.ProductCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == categoryId);
            if (category is null) return ApiProblems.Validation("CategoryId", "دسته پیدا نشد.");
            if (!category.IsActive) return ApiProblems.Validation("CategoryId", "این دسته غیرفعال است.");
        }

        var updated = new List<Guid>();
        var failed = new List<BulkFailure>();
        foreach (var target in request.Items)
        {
            // هر قلم جدا ذخیره می‌شود (تاریخچه‌ی هر قلم جدا)؛ پس از هر شکست ردیابی پاک می‌شود.
            db.ChangeTracker.Clear();
            var product = await db.Products.FirstOrDefaultAsync(p => p.Id == target.Id);
            if (product is null) { failed.Add(new BulkFailure(target.Id, "قلم پیدا نشد.")); continue; }
            if (product.CategoryId == request.CategoryId) { updated.Add(target.Id); continue; }

            db.ExpectVersion(product, target.Version);
            product.CategoryId = request.CategoryId;
            try
            {
                await db.SaveChangesAsync();
                updated.Add(target.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                failed.Add(new BulkFailure(target.Id, $"«{product.Name}» در این فاصله تغییر کرده است؛ فهرست را تازه کنید."));
            }
            catch (DbUpdateException)
            {
                failed.Add(new BulkFailure(target.Id, $"«{product.Name}» ذخیره نشد؛ کمی بعد دوباره تلاش کنید."));
            }
        }

        db.ChangeTracker.Clear();
        return Results.Ok(new BulkCategoryResult(updated, failed));
    }

    // ترتیب نام در حافظه، نه در SQLite (مقایسه‌ی متن فارسی در SQLite ممنوع است).
    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var categories = await db.ProductCategories.AsNoTracking().ToListAsync();
        var counts = await CountsAsync(db);
        return Results.Ok(categories
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .Select(c => ToItem(c, counts.GetValueOrDefault(c.Id)))
            .ToList());
    }

    private static async Task<Dictionary<Guid, int>> CountsAsync(WarehouseDbContext db) =>
        await db.Products.AsNoTracking()
            .Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

    private static CategoryItem ToItem(ProductCategory c, int productCount) => new(c.Id, c.Name, c.IsActive, productCount, c.Version);

    private static async Task<IResult> SaveAsync(WarehouseDbContext db, ProductCategory category)
    {
        try
        {
            await db.SaveChangesAsync();
            var count = await db.Products.CountAsync(p => p.CategoryId == category.Id);
            return Results.Ok(ToItem(category, count));
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

    private static async Task<IResult> CreateAsync(SaveCategoryRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CategoryRules.NameError(name) is { } error) return ApiProblems.Validation("Name", error);

        var key = TextNormalizer.Key(name)!;
        if (await db.ProductCategories.AnyAsync(c => c.NameKey == key)) return ApiProblems.Conflict(Duplicate);

        var category = new ProductCategory { Id = Guid.NewGuid(), Name = name!, NameKey = key, IsActive = request.IsActive };
        db.ProductCategories.Add(category);
        return await SaveAsync(db, category);
    }

    private static async Task<IResult> UpdateAsync(Guid id, SaveCategoryRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CategoryRules.NameError(name) is { } error) return ApiProblems.Validation("Name", error);

        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return ApiProblems.NotFound();

        var key = TextNormalizer.Key(name)!;
        if (await db.ProductCategories.AnyAsync(c => c.NameKey == key && c.Id != id)) return ApiProblems.Conflict(Duplicate);

        category.Name = name!;
        category.NameKey = key;
        category.IsActive = request.IsActive;
        db.ExpectVersion(category, request.Version);
        return await SaveAsync(db, category);
    }

    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.CategoryId == id)
            .Select(p => new { p.Id, p.Name })
            .ToListAsync();
        var items = products.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new DependencyItem("قلم", p.Name, p.Id)).ToList();

        // دوره و نوبت انبارگردانی حذف ندارند، پس دسته‌ی دامنه‌شان هم حذف نمی‌شود.
        var periods = await db.StocktakePeriodCategories.AsNoTracking().Where(c => c.CategoryId == id)
            .Join(db.StocktakePeriods, c => c.PeriodId, p => p.Id, (c, p) => p.Name).ToListAsync();
        items.AddRange(periods.Order(StringComparer.Ordinal).Select(n => new DependencyItem("دوره‌ی انبارگردانی", n)));
        var sessions = await db.Stocktakes.AsNoTracking().CountAsync(s => s.CategoryId == id);
        if (sessions > 0) items.Add(new DependencyItem("نوبت انبارگردانی", $"{sessions} نوبت"));
        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.ProductCategories.AnyAsync(c => c.Id == id)
            ? Results.Ok(await FindDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> DeleteAsync(Guid id, WarehouseDbContext db)
    {
        var category = await db.ProductCategories.FirstOrDefaultAsync(c => c.Id == id);
        if (category is null) return ApiProblems.NotFound();

        if (!(await FindDependenciesAsync(db, id)).CanDelete) return ApiProblems.Conflict(InUse);

        db.ProductCategories.Remove(category);
        try
        {
            await db.SaveChangesAsync();
            return Results.Ok(new DependencyList());
        }
        catch (DbUpdateException)
        {
            // قلمی که هم‌زمان به این دسته وصل شد؛ کلید خارجی Restrict جلوی حذف را گرفت.
            return ApiProblems.Conflict(InUse);
        }
    }
}
