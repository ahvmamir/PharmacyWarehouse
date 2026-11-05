using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Stock;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Operations;

// داده‌ی آزمایشی آشکارا ساختگی برای دیدن صفحه‌ها با حجم واقعی (بخش ۱۵.۱ spec). همه‌چیز از رهگیر و StockLedger می‌گذرد،
// پس تاریخچه و ثابت OnHand = جمع حرکت‌ها درست می‌ماند. مقدارها قاعده‌مندند، نه تصادفی، تا هر اجرا یکسان باشد.
public static class TestDataSeeder
{
    public const string NamePrefix = "داروی آزمایشی";
    public const int ProductCount = 100;

    private const string RootName = "انبار آزمایشی";
    private static readonly string[] Packs = ["۳۰ عدد", "۱۰۰ سی‌سی", "۱۰ عدد", "۵۰ گرم"];

    public sealed record SeedResult(int Products, int Batches);

    // null یعنی قبلاً اجرا شده و چیزی ساخته نشد.
    public static async Task<SeedResult?> SeedAsync(WarehouseDbContext db, StockLedger ledger, DateTime nowUtc)
    {
        var prefixKey = TextNormalizer.Key(NamePrefix)!;
        if (await db.Products.AnyAsync(p => p.NameKey.StartsWith(prefixKey))) return null;

        var today = LocalDay.Today(nowUtc);
        var leaves = await EnsureLocationsAsync(db);

        var batchCount = 0;
        for (var n = 1; n <= ProductCount; n++)
        {
            // هر ۲۰ قلم یکی بدون نیاز به انقضا، هر ۱۰ قلم یکی بدون موجودی، و بقیه با انقضای متنوع.
            var requiresExpiry = n % 20 != 7;
            var product = NewProduct(n, leaves[n % leaves.Count].Id, requiresExpiry);
            db.Products.Add(product);
            db.ProductBarcodes.Add(new ProductBarcode
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                Code = BarcodeOf(n),
                CodeKey = TextNormalizer.ScanKey(BarcodeOf(n))!
            });

            if (n % 10 == 0) continue;

            batchCount++;
            AddBatch(db, ledger, product, BatchNumber(batchCount), requiresExpiry ? ExpiryOf(n, today) : null, 1 + n % 25, nowUtc, null);

            // هر چهارمین قلم بچ دوم در محل دیگر دارد تا تقسیم FEFO و «تغییر محل» دیده شود.
            if (n % 4 != 0) continue;

            batchCount++;
            AddBatch(db, ledger, product, BatchNumber(batchCount), requiresExpiry ? today.AddMonths(14 + n % 6) : null, 2 + n % 7, nowUtc, leaves[(n + 1) % leaves.Count].Id);
        }

        await db.SaveChangesAsync();
        return new SeedResult(ProductCount, batchCount);
    }

    private static Product NewProduct(int n, Guid locationId, bool requiresExpiry)
    {
        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = TextNormalizer.Clean($"{NamePrefix} {n:000}")!,
            Brand = TextNormalizer.Clean($"برند آزمایشی {1 + n % 5}"),
            PackDescription = TextNormalizer.Clean(Packs[n % Packs.Length]),
            LocationId = locationId,
            // هر سه قلم یکی حداقل بزرگ دارد تا «کمتر از حداقل» پر شود؛ هر پنج قلم یکی بی‌حداقل است.
            MinStock = n % 5 == 0 ? null : n % 3 == 0 ? 30 : 2,
            RequiresExpiry = requiresExpiry,
            CreatedAtUtc = DateTime.UtcNow
        };
        product.NameKey = TextNormalizer.Key(product.Name)!;
        product.BrandKey = TextNormalizer.Key(product.Brand) ?? "";
        product.PackKey = TextNormalizer.Key(product.PackDescription) ?? "";
        product.SearchText = ProductEndpoints.BuildSearchText(product, [BarcodeOf(n)]);
        return product;
    }

    private static void AddBatch(WarehouseDbContext db, StockLedger ledger, Product product, string number, DateOnly? expiry, int quantity, DateTime nowUtc, Guid? locationId)
    {
        var batch = new Batch
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            BatchNumber = number,
            BatchKey = TextNormalizer.ScanKey(number)!,
            ExpiryDate = expiry,
            ExpiryPrecision = ExpiryPrecision.Day,
            ExpiryCalendar = CalendarKind.Gregorian,
            LocationId = locationId,
            CreatedAtUtc = nowUtc
        };
        db.Batches.Add(batch);
        ledger.Post(batch, StockMovementKind.Import, quantity, nowUtc, "داده‌ی آزمایشی");
    }

    // پراکنده در منقضی، نزدیک، میان‌مدت و دور؛ هر دسته‌ی داشبورد پر می‌شود.
    private static DateOnly ExpiryOf(int n, DateOnly today) => (n % 4) switch
    {
        0 => today.AddDays(-(5 + n % 60)),
        1 => today.AddDays(10 + n % 70),
        2 => today.AddMonths(4 + n % 2),
        _ => today.AddMonths(9 + n % 24)
    };

    private static string BarcodeOf(int n) => $"TEST{n:0000000000}";

    private static string BatchNumber(int n) => $"TEST-{n:0000}";

    private static async Task<List<StorageLocation>> EnsureLocationsAsync(WarehouseDbContext db)
    {
        var rootKey = TextNormalizer.Key(RootName)!;
        var root = await db.StorageLocations.FirstOrDefaultAsync(l => l.ParentKey == "" && l.NameKey == rootKey);
        if (root is null)
        {
            root = NewLocation(RootName, null, 0);
            db.StorageLocations.Add(root);
        }

        var leaves = await db.StorageLocations.Where(l => l.ParentId == root.Id).ToListAsync();
        if (leaves.Count > 0) return leaves;

        for (var i = 1; i <= 12; i++)
        {
            var leaf = NewLocation(TextNormalizer.Clean($"قفسه‌ی آزمایشی {i:00}")!, root.Id, i);
            db.StorageLocations.Add(leaf);
            leaves.Add(leaf);
        }

        return leaves;
    }

    private static StorageLocation NewLocation(string name, Guid? parentId, int order) => new()
    {
        Id = Guid.NewGuid(),
        ParentId = parentId,
        ParentKey = StorageLocation.KeyOf(parentId),
        Name = name,
        NameKey = TextNormalizer.Key(name)!,
        SortOrder = order
    };
}
