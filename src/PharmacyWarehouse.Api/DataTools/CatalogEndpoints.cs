using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Import;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.DataTools;

// فهرست مرجع TTAC (بخش ۱۵.۴ spec): فقط فایلی که امیر دستی دانلود می‌کند؛ هیچ اتصالی به سامانه‌ی بیرونی نیست.
// فهرست هرگز قلم، بچ یا حرکت نمی‌سازد؛ جست‌وجو فقط فرم تعریف قلم را پر می‌کند.
public static class CatalogEndpoints
{
    private const long MaxFileBytes = 20 * 1024 * 1024;
    private const int MaxResults = 20;
    // [پیشنهاد] هشدار «فهرست قدیمی» در صفحه‌ی /catalog.
    private const int StaleAfterDays = 90;

    public static void MapCatalogEndpoints(this WebApplication app)
    {
        // CSRF با هدر سفارشی ما انجام می‌شود (CsrfHeaderMiddleware)؛ antiforgery فرم پیش‌فرض برای فایل لازم نیست.
        app.MapPost(ApiRoutes.CatalogImport, ImportAsync).RequireAuthorization(Permissions.DataImport).DisableAntiforgery();
        app.MapGet(ApiRoutes.CatalogStatus, StatusAsync).RequireAuthorization(Permissions.DataImport);
        app.MapGet(ApiRoutes.Catalog + "/search", SearchAsync).RequireAuthorization(Permissions.ProductsManage);
    }

    // کلید جست‌وجو: نام فارسی، نام انگلیسی و ژنریک.
    internal static string SearchKeyOf(CatalogRow row) =>
        TextNormalizer.SearchKey(string.Join(" ", new[] { row.PersianBrandName, row.BrandName, row.GenericName }.Where(s => s is not null))) ?? "";

    // جایگزینی کامل در یک تراکنش: شکست هر مرحله یعنی فهرست قبلی دست‌نخورده می‌ماند.
    private static async Task<IResult> ImportAsync(IFormFile? file, WarehouseDbContext db, AuditContext audit, ILoggerFactory loggers)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        if (file is null || file.Length == 0) return ApiProblems.Validation("File", "فایل CSV را انتخاب کنید.");
        if (file.Length > MaxFileBytes) return ApiProblems.Validation("File", "فایل بیش از ۲۰ مگابایت است.");

        CatalogReadResult read;
        await using (var stream = file.OpenReadStream())
        {
            read = CatalogCsvReader.Read(stream);
        }

        if (read.MissingColumn is { } missing) return ApiProblems.Validation("File", $"ستون «{missing}» در سرستون فایل نیست؛ فایل CSV خروجی TTAC را انتخاب کنید.");
        if (read.Rows.Count == 0) return ApiProblems.Validation("File", "هیچ ردیف معتبری (با GTIN درست) در فایل نبود؛ فهرست قبلی عوض نشد.");

        var fileName = TextNormalizer.Clean(Path.GetFileName(file.FileName)) ?? "—";
        if (fileName.Length > CatalogImport.MaxFileName) fileName = fileName[..CatalogImport.MaxFileName];

        db.ChangeTracker.AutoDetectChangesEnabled = false;
        await using var transaction = await db.Database.BeginTransactionAsync();
        // CatalogItem عمداً IAudited نیست؛ ExecuteDelete فقط روی همین جدول مجاز است (CLAUDE.md).
        await db.CatalogItems.ExecuteDeleteAsync();
        db.CatalogItems.AddRange(read.Rows.Select(r => new CatalogItem
        {
            Id = Guid.NewGuid(),
            Irc = r.Irc,
            Gtin = r.Gtin,
            GenericCode = r.GenericCode,
            GenericName = r.GenericName,
            BrandName = r.BrandName,
            PersianBrandName = r.PersianBrandName,
            LicenseOwner = r.LicenseOwner,
            Producer = r.Producer,
            ProducerCountry = r.ProducerCountry,
            PackageCount = r.PackageCount,
            ConsumerPrice = r.ConsumerPrice,
            IsTemporaryIrc = r.IsTemporaryIrc,
            SearchKey = SearchKeyOf(r)
        }));
        db.CatalogImports.Add(new CatalogImport
        {
            Id = Guid.NewGuid(), FileName = fileName, Inserted = read.Rows.Count, Empty = read.Empty, Invalid = read.Invalid,
            DuplicateGtin = read.DuplicateGtin, AtUtc = DateTime.UtcNow, UserId = userId
        });
        db.ChangeTracker.DetectChanges();
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        loggers.CreateLogger("Catalog").LogInformation("Catalog replaced by user {UserId}: {Inserted} rows", userId, read.Rows.Count);
        return Results.Ok(new CatalogImportResult(read.Rows.Count, read.Empty, read.Invalid, read.DuplicateGtin));
    }

    private static async Task<IResult> StatusAsync(WarehouseDbContext db)
    {
        var count = await db.CatalogItems.CountAsync();
        var last = await db.CatalogImports.AsNoTracking().OrderByDescending(i => i.AtUtc).FirstOrDefaultAsync();
        var user = last is null ? null : await db.Users.AsNoTracking().Where(u => u.Id == last.UserId).Select(u => u.UserName).FirstOrDefaultAsync();
        return Results.Ok(new CatalogStatus(count, last?.AtUtc, last?.FileName, user, StaleAfterDays));
    }

    // رقم ۸ تا ۱۴ یا رشته‌ی GS1 ← تطبیق GTIN؛ بقیه ← همه‌ی کلمه‌ها در SearchKey (ستون کلید، نه مقایسه‌ی مستقیم متن فارسی).
    private static async Task<IResult> SearchAsync(string? q, WarehouseDbContext db)
    {
        if (string.IsNullOrWhiteSpace(q)) return Results.Ok(new List<CatalogSuggestion>());

        List<CatalogItem> items;
        var gtin = Gs1Scan.Parse(q)?.Gtin ?? CatalogCsvReader.NormalizeGtin(q);
        if (gtin is not null)
        {
            items = await db.CatalogItems.AsNoTracking().Where(c => c.Gtin == gtin).Take(MaxResults).ToListAsync();
        }
        else
        {
            var words = (TextNormalizer.SearchKey(q) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 2).Take(5).ToList();
            if (words.Count == 0) return Results.Ok(new List<CatalogSuggestion>());

            var query = db.CatalogItems.AsNoTracking();
            foreach (var word in words) query = query.Where(c => c.SearchKey.Contains(word));
            items = await query.OrderBy(c => c.SearchKey).Take(MaxResults).ToListAsync();
        }

        // GTIN که از قبل بارکد قلمی است: همان قلم گفته می‌شود تا قلم تکراری ساخته نشود.
        var keys = items.SelectMany(i => Gs1Scan.CodeVariants(i.Gtin)).Distinct().ToList();
        var owners = await (
            from b in db.ProductBarcodes.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where keys.Contains(b.CodeKey)
            select new { b.CodeKey, p.Id, p.Name }).ToListAsync();

        return Results.Ok(items.Select(i =>
        {
            var variants = Gs1Scan.CodeVariants(i.Gtin);
            var owner = owners.FirstOrDefault(o => variants.Contains(o.CodeKey));
            return new CatalogSuggestion(i.Id, i.Gtin, i.Irc, CatalogMapping.Name(i.PersianBrandName, i.BrandName), i.Producer,
                CatalogMapping.Pack(i.PackageCount), i.GenericName, i.BrandName, i.ProducerCountry, i.ConsumerPrice, i.IsTemporaryIrc,
                owner?.Id, owner?.Name);
        }).ToList());
    }
}
