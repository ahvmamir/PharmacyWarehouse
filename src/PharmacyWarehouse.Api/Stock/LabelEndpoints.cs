using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Stock;

// برچسب اقلام بدون بارکد کارخانه (بخش ۹.۳ spec). برچسب محل داده‌ی سروری لازم ندارد و از شناسه ساخته می‌شود.
public static class LabelEndpoints
{
    public static void MapLabelEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.LabelProducts, ListAsync).RequireAuthorization(Permissions.ProductsManage);
        app.MapPost(ApiRoutes.LabelProductsGenerate, GenerateAsync).RequireAuthorization(Permissions.ProductsManage);
    }

    // قلم فعال بدون هیچ بارکد، یا با کد داخلی (برای چاپ دوباره). قلم با بارکد کارخانه برچسب لازم ندارد.
    private static async Task<List<ProductLabelItem>> LoadAsync(WarehouseDbContext db)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync();
        var barcodes = (await db.ProductBarcodes.AsNoTracking().ToListAsync()).ToLookup(b => b.ProductId);

        return products
            .Where(p => !barcodes[p.Id].Any() || barcodes[p.Id].All(b => InternalBarcode.IsInternal(b.CodeKey)))
            .OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Brand ?? "", StringComparer.Ordinal).ThenBy(p => p.Id)
            .Select(p => new ProductLabelItem(p.Id, p.Name, p.Brand, p.PackDescription, barcodes[p.Id].FirstOrDefault()?.Code))
            .ToList();
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db) => Results.Ok(await LoadAsync(db));

    // قلمی که بارکد دارد نادیده گرفته می‌شود؛ پس فراخوانی دوباره کد تازه نمی‌سازد.
    private static async Task<IResult> GenerateAsync(GenerateLabelsRequest request, WarehouseDbContext db)
    {
        if (request.ProductIds is not { Count: > 0 } ids) return ApiProblems.Validation("ProductIds", "قلمی انتخاب نشده است.");
        if (ids.Count > GenerateLabelsRequest.MaxProducts) return ApiProblems.Validation("ProductIds", $"حداکثر {GenerateLabelsRequest.MaxProducts} قلم در یک بار.");

        var products = await db.Products.Where(p => ids.Contains(p.Id) && p.IsActive).OrderBy(p => p.Name).ThenBy(p => p.Id).ToListAsync();
        var keys = await db.ProductBarcodes.AsNoTracking().Select(b => new { b.ProductId, b.CodeKey }).ToListAsync();
        var withCode = keys.Select(k => k.ProductId).ToHashSet();
        var next = InternalBarcode.NextNumber(keys.Select(k => k.CodeKey));

        foreach (var product in products.Where(p => !withCode.Contains(p.Id)))
        {
            var code = InternalBarcode.Format(next++);
            db.ProductBarcodes.Add(new ProductBarcode { Id = Guid.NewGuid(), ProductId = product.Id, Code = code, CodeKey = TextNormalizer.ScanKey(code)! });
            product.SearchText = ProductEndpoints.BuildSearchText(product, [code]);
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
            return ApiProblems.Conflict("کد داخلی هم‌زمان ساخته شد؛ دوباره تلاش کنید.");
        }

        return Results.Ok(await LoadAsync(db));
    }
}
