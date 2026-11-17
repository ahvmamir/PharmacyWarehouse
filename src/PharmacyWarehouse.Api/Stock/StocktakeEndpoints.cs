using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// انبارگردانی دوره‌ای (بخش ۷.۶ spec): شمارش تعداد هر بچ، اختلاف در پایان نوبت، و تأیید هر اختلاف با یک حرکت Adjustment.
public static class StocktakeEndpoints
{
    private const int MaxList = 50;

    public static void MapStocktakeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Stocktakes).RequireAuthorization(Permissions.StockAdjust);
        group.MapGet("", ListAsync);
        group.MapPost("", StartAsync);
        group.MapGet("{id:guid}", GetAsync);
        group.MapGet("{id:guid}/batches", BatchesAsync);
        group.MapPost("{id:guid}/counts", CountAsync);
        group.MapPost("{id:guid}/close", CloseAsync);
        group.MapPost("{id:guid}/counts/{countId:guid}/approve", ApproveAsync);
    }

    private static List<LocationNode> Nodes(IEnumerable<StorageLocation> locations) =>
        locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)).ToList();

    internal sealed record ScopedBatch(Batch Batch, Product Product, Guid? EffectiveLocation);

    // محل مؤثر بچ: محل خودش، وگرنه محل قلم. فهرست‌ها کوچک‌اند و مثل بقیه‌ی پروژه در حافظه فیلتر می‌شوند.
    // categories خالی یعنی همه‌ی دسته‌ها؛ دامنه‌ی نوبت = زیرشاخه‌ی محل ∩ دسته‌ها (بخش ۱۵.۳ spec).
    internal static async Task<List<ScopedBatch>> ScopedBatchesAsync(WarehouseDbContext db, HashSet<Guid>? scope, IReadOnlyCollection<Guid> categories, Guid? productId = null)
    {
        var rows = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where productId == null || p.Id == productId
            select new { Batch = b, Product = p }).ToListAsync();

        return rows
            .Select(r => new ScopedBatch(r.Batch, r.Product, r.Batch.LocationId ?? r.Product.LocationId))
            .Where(r => StocktakeRules.InScope(r.EffectiveLocation, r.Product.CategoryId, scope, categories))
            .ToList();
    }

    // دسته‌های نوبت: دسته‌ی خودش، وگرنه دسته‌های دوره (خالی = همه).
    internal static async Task<List<Guid>> SessionCategoriesAsync(WarehouseDbContext db, Stocktake stocktake) =>
        stocktake.CategoryId is { } category
            ? [category]
            : await db.StocktakePeriodCategories.AsNoTracking().Where(c => c.PeriodId == stocktake.PeriodId).Select(c => c.CategoryId).ToListAsync();

    internal static string? ExpiryText(Batch b) =>
        b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null;

    private static string? PathOf(IReadOnlyDictionary<Guid, string> paths, Guid? id) =>
        id is { } g && paths.TryGetValue(g, out var path) ? path : null;

    internal static async Task<Dictionary<Guid, string>> UserNamesAsync(WarehouseDbContext db, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "—");
    }

    // آخرین شمارش هر بچ؛ شمارش‌های قبلی همان بچ جایگزین شده‌اند.
    internal static List<StocktakeCount> Latest(IEnumerable<StocktakeCount> counts) => counts
        .GroupBy(c => c.BatchId)
        .Select(g => g.OrderByDescending(c => c.AtUtc).ThenByDescending(c => c.Id).First())
        .ToList();

    private static async Task<IResult> ListAsync(WarehouseDbContext db, Guid? periodId = null) =>
        Results.Ok(await ListItemsAsync(db, periodId));

    internal static async Task<List<StocktakeListItem>> ListItemsAsync(WarehouseDbContext db, Guid? periodId)
    {
        var stocktakes = await db.Stocktakes.AsNoTracking()
            .Where(s => periodId == null || s.PeriodId == periodId)
            .OrderBy(s => s.ClosedAtUtc != null).ThenByDescending(s => s.StartedAtUtc).Take(periodId == null ? MaxList : 500).ToListAsync();
        var ids = stocktakes.Select(s => s.Id).ToList();
        var counts = (await db.StocktakeCounts.AsNoTracking().Where(c => ids.Contains(c.StocktakeId)).ToListAsync()).ToLookup(c => c.StocktakeId);
        var paths = LocationTree.Paths(Nodes(await db.StorageLocations.AsNoTracking().ToListAsync()));
        var names = await UserNamesAsync(db, stocktakes.Select(s => s.StartedById));
        var periods = await db.StocktakePeriods.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name);
        var categories = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);

        return stocktakes.Select(s =>
        {
            var latest = Latest(counts[s.Id]);
            return new StocktakeListItem(s.Id, s.LocationId, PathOf(paths, s.LocationId) ?? "—", names.GetValueOrDefault(s.StartedById, "—"),
                s.StartedAtUtc, s.ClosedAtUtc, latest.Count, latest.Count(c => c.Counted != c.ExpectedOnHand), s.Version,
                s.PeriodId, periods.GetValueOrDefault(s.PeriodId), s.CategoryId is { } c ? categories.GetValueOrDefault(c) : null);
        }).ToList();
    }

    private static async Task<IResult> StartAsync(StartStocktakeRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        if (locations.All(l => l.Id != request.LocationId)) return ApiProblems.Validation("LocationId", "محل را انتخاب کنید.");

        var usable = LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
        if (!usable.Contains(request.LocationId)) return ApiProblems.Validation("LocationId", "این محل غیرفعال است.");

        // هر نوبت زیر یک دوره‌ی باز است و محل و دسته‌اش داخل دامنه‌ی همان دوره (بخش ۱۵.۳ spec).
        var nodes = Nodes(locations);
        if (request.PeriodId is not { } periodId) return ApiProblems.Validation("PeriodId", "دوره‌ی انبارگردانی را انتخاب کنید.");
        var period = await db.StocktakePeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == periodId);
        if (period is null) return ApiProblems.Validation("PeriodId", "دوره پیدا نشد.");
        if (period.ClosedAtUtc is not null) return ApiProblems.Conflict("این دوره بسته شده است؛ دوره‌ی باز دیگری انتخاب کنید.");

        var periodLocations = await db.StocktakePeriodLocations.AsNoTracking().Where(l => l.PeriodId == periodId).Select(l => l.LocationId).ToListAsync();
        if (!StocktakeRules.LocationInPeriod(request.LocationId, periodLocations, id => LocationTree.Subtree(nodes, id)))
        {
            return ApiProblems.Validation("LocationId", "این محل در دامنه‌ی دوره نیست.");
        }

        if (request.CategoryId is { } categoryId)
        {
            if (!await db.ProductCategories.AnyAsync(c => c.Id == categoryId)) return ApiProblems.Validation("CategoryId", "دسته پیدا نشد.");
            var periodCategories = await db.StocktakePeriodCategories.AsNoTracking().Where(c => c.PeriodId == periodId).Select(c => c.CategoryId).ToListAsync();
            if (periodCategories.Count > 0 && !periodCategories.Contains(categoryId)) return ApiProblems.Validation("CategoryId", "این دسته در دامنه‌ی دوره نیست.");
        }

        // نوبت باز روی همان محل، والد یا زیرشاخه‌ی آن یعنی یک بچ در دو نوبت شمرده می‌شود.
        var scope = LocationTree.Subtree(nodes, request.LocationId);
        var open = await db.Stocktakes.AsNoTracking().Where(s => s.ClosedAtUtc == null).Select(s => s.LocationId).ToListAsync();
        if (open.Any(other => scope.Contains(other) || LocationTree.Subtree(nodes, other).Contains(request.LocationId)))
        {
            return ApiProblems.Conflict("برای این محل، والد یا زیرشاخه‌ی آن نوبت بازی هست؛ اول همان را ببندید.");
        }

        var stocktake = new Stocktake
        {
            Id = Guid.NewGuid(), PeriodId = periodId, CategoryId = request.CategoryId, LocationId = request.LocationId,
            StartedById = userId, StartedAtUtc = DateTime.UtcNow
        };
        db.Stocktakes.Add(stocktake);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict("هم‌زمان نوبت دیگری برای این محل شروع شد.");
        }

        return Results.Ok(await ToDetailAsync(db, stocktake));
    }

    private static async Task<StocktakeDetail> ToDetailAsync(WarehouseDbContext db, Stocktake stocktake)
    {
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var nodes = Nodes(locations);
        var paths = LocationTree.Paths(nodes);
        var latest = Latest(await db.StocktakeCounts.AsNoTracking().Where(c => c.StocktakeId == stocktake.Id).ToListAsync());
        var scoped = await ScopedBatchesAsync(db, LocationTree.Subtree(nodes, stocktake.LocationId), await SessionCategoriesAsync(db, stocktake));
        var period = await db.StocktakePeriods.AsNoTracking().FirstAsync(p => p.Id == stocktake.PeriodId);
        var categoryName = stocktake.CategoryId is { } categoryId
            ? await db.ProductCategories.AsNoTracking().Where(c => c.Id == categoryId).Select(c => c.Name).FirstOrDefaultAsync()
            : null;

        // بچ شمرده‌شده ممکن است پس از شمارش جابه‌جا شده باشد؛ پس جدا و بدون فیلتر محل خوانده می‌شود.
        var countedIds = latest.Select(c => c.BatchId).ToList();
        var counted = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where countedIds.Contains(b.Id)
            select new { Batch = b, Product = p }).ToDictionaryAsync(r => r.Batch.Id);
        var names = await UserNamesAsync(db, latest.Select(c => c.UserId).Append(stocktake.StartedById));

        return new StocktakeDetail
        {
            Id = stocktake.Id,
            LocationId = stocktake.LocationId,
            LocationPath = PathOf(paths, stocktake.LocationId) ?? "—",
            StartedByName = names.GetValueOrDefault(stocktake.StartedById, "—"),
            StartedAtUtc = stocktake.StartedAtUtc,
            ClosedAtUtc = stocktake.ClosedAtUtc,
            Version = stocktake.Version,
            PeriodId = period.Id,
            PeriodName = period.Name,
            PeriodOpen = period.ClosedAtUtc is null,
            CategoryName = categoryName,
            Counts = latest
                .OrderByDescending(c => c.Counted != c.ExpectedOnHand).ThenByDescending(c => c.AtUtc).ThenBy(c => c.Id)
                .Select(c =>
                {
                    var row = counted[c.BatchId];
                    return new StocktakeCountItem(c.Id, c.BatchId, row.Product.Id, row.Product.Name, row.Product.Brand, row.Batch.BatchNumber,
                        ExpiryText(row.Batch), PathOf(paths, row.Batch.LocationId ?? row.Product.LocationId), c.ExpectedOnHand, c.Counted,
                        c.Counted - c.ExpectedOnHand, c.AtUtc, names.GetValueOrDefault(c.UserId, "—"), c.AdjustmentId is not null, c.Version);
                })
                .ToList(),
            Missing = scoped
                .Where(s => s.Batch.OnHand > 0 && !counted.ContainsKey(s.Batch.Id))
                .OrderBy(s => s.Product.Name, StringComparer.Ordinal).ThenBy(s => s.Batch.BatchNumber, StringComparer.Ordinal)
                .Select(s => ToBatchItem(s, paths))
                .ToList()
        };
    }

    private static StocktakeBatchItem ToBatchItem(ScopedBatch s, IReadOnlyDictionary<Guid, string> paths) =>
        new(s.Batch.Id, s.Product.Id, s.Product.Name, s.Product.Brand, s.Batch.BatchNumber, ExpiryText(s.Batch), PathOf(paths, s.EffectiveLocation), s.Batch.OnHand);

    private static async Task<IResult> GetAsync(Guid id, WarehouseDbContext db)
    {
        var stocktake = await db.Stocktakes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        return stocktake is null ? ApiProblems.NotFound() : Results.Ok(await ToDetailAsync(db, stocktake));
    }

    // بچ‌های یک قلم که در دامنه‌ی این نوبت‌اند؛ صفحه‌ی شمارش فقط همین‌ها را نشان می‌دهد.
    private static async Task<IResult> BatchesAsync(Guid id, Guid productId, WarehouseDbContext db)
    {
        var stocktake = await db.Stocktakes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (stocktake is null) return ApiProblems.NotFound();

        var nodes = Nodes(await db.StorageLocations.AsNoTracking().ToListAsync());
        var paths = LocationTree.Paths(nodes);
        var scoped = await ScopedBatchesAsync(db, LocationTree.Subtree(nodes, stocktake.LocationId), await SessionCategoriesAsync(db, stocktake), productId);
        return Results.Ok(scoped
            .OrderBy(s => s.Batch.ExpiryDate is null).ThenBy(s => s.Batch.ExpiryDate).ThenBy(s => s.Batch.BatchNumber, StringComparer.Ordinal)
            .Select(s => ToBatchItem(s, paths))
            .ToList());
    }

    // شمارش فقط اضافه می‌شود و Version نوبت را جلو نمی‌برد؛ وگرنه دو نفر که هم‌زمان می‌شمرند مدام ۴۰۹ می‌گرفتند.
    private static async Task<IResult> CountAsync(Guid id, StocktakeCountRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        if (request.Counted < 0) return ApiProblems.Validation("Counted", "تعداد شمرده‌شده نمی‌تواند منفی باشد.");

        var stocktake = await db.Stocktakes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (stocktake is null) return ApiProblems.NotFound();
        if (stocktake.ClosedAtUtc is not null) return ApiProblems.Conflict("این نوبت بسته شده است.");
        if (await db.StocktakePeriods.AnyAsync(p => p.Id == stocktake.PeriodId && p.ClosedAtUtc != null)) return ApiProblems.Conflict("دوره‌ی این نوبت بسته شده است.");

        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatchId);
        if (batch is null) return ApiProblems.Validation("BatchId", "بچ پیدا نشد.");

        var nodes = Nodes(await db.StorageLocations.AsNoTracking().ToListAsync());
        var product = await db.Products.AsNoTracking().Where(p => p.Id == batch.ProductId).Select(p => new { p.LocationId, p.CategoryId }).FirstAsync();
        if ((batch.LocationId ?? product.LocationId) is not { } location || !LocationTree.Subtree(nodes, stocktake.LocationId).Contains(location))
        {
            return ApiProblems.Validation("BatchId", "این بچ در محل این نوبت نیست.");
        }

        var categories = await SessionCategoriesAsync(db, stocktake);
        if (categories.Count > 0 && (product.CategoryId is not { } category || !categories.Contains(category)))
        {
            return ApiProblems.Validation("BatchId", "دسته‌ی این قلم در دامنه‌ی این نوبت نیست.");
        }

        db.StocktakeCounts.Add(new StocktakeCount
        {
            Id = Guid.NewGuid(),
            StocktakeId = id,
            BatchId = batch.Id,
            ExpectedOnHand = batch.OnHand, // در لحظه‌ی شمارش، نه شروع نوبت
            Counted = request.Counted,
            AtUtc = DateTime.UtcNow,
            UserId = userId
        });
        await db.SaveChangesAsync();
        return Results.Ok(await ToDetailAsync(db, stocktake));
    }

    private static async Task<IResult> CloseAsync(Guid id, VersionRequest request, WarehouseDbContext db, AuditContext audit)
    {
        var stocktake = await db.Stocktakes.FirstOrDefaultAsync(s => s.Id == id);
        if (stocktake is null) return ApiProblems.NotFound();
        if (stocktake.Version != request.Version) return ApiProblems.VersionConflict();
        if (stocktake.ClosedAtUtc is not null) return ApiProblems.Conflict("این نوبت قبلاً بسته شده است.");

        db.ExpectVersion(stocktake, request.Version);
        stocktake.ClosedAtUtc = DateTime.UtcNow;
        stocktake.ClosedById = audit.UserId;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(await ToDetailAsync(db, stocktake));
    }

    // تفاوت همان «شمرده‌شده منهای مورد انتظار در لحظه‌ی شمارش» است، نه منهای موجودی فعلی؛
    // برداشت‌های بعد از شمارش واقعی‌اند و نباید با اصلاح خنثی شوند.
    private static async Task<IResult> ApproveAsync(Guid id, Guid countId, VersionRequest request, WarehouseDbContext db, StockLedger ledger)
    {
        var stocktake = await db.Stocktakes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        var count = await db.StocktakeCounts.FirstOrDefaultAsync(c => c.Id == countId && c.StocktakeId == id);
        if (stocktake is null || count is null) return ApiProblems.NotFound();
        if (count.Version != request.Version) return ApiProblems.VersionConflict();

        if (await PrepareAdjustmentAsync(db, ledger, stocktake, count, request.Version) is { } problem) return ApiProblems.Conflict(problem);

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
            return ApiProblems.Conflict("این اختلاف قبلاً تأیید شده است.");
        }

        return Results.Ok(await ToDetailAsync(db, stocktake));
    }

    // قاعده‌های اصلاح یک شمارش، مشترک «تأیید» نوبت و «رفع گروهی» دوره. ذخیره نمی‌کند؛ پیام خطا یا null برمی‌گرداند.
    internal static async Task<string?> PrepareAdjustmentAsync(WarehouseDbContext db, StockLedger ledger, Stocktake stocktake, StocktakeCount count, int version)
    {
        if (stocktake.ClosedAtUtc is null) return "اختلاف‌ها پس از بستن نوبت تأیید می‌شوند.";
        if (count.AdjustmentId is not null) return "این اختلاف قبلاً تأیید شده است.";
        if (count.DismissedAtUtc is not null) return "از این اختلاف صرف‌نظر شده است.";

        var difference = count.Counted - count.ExpectedOnHand;
        if (difference == 0) return "این شمارش اختلافی ندارد.";

        var newer = await db.StocktakeCounts.AnyAsync(c => c.BatchId == count.BatchId && c.Id != count.Id && c.AtUtc >= count.AtUtc);
        if (newer) return "برای این بچ شمارش تازه‌تری هست؛ این شمارش دیگر معتبر نیست.";

        // اصلاح دستی پس از شمارش احتمالاً همین اختلاف را درست کرده؛ اعمال دوباره موجودی را دو بار عوض می‌کرد.
        var adjustedLater = await db.StockMovements.AnyAsync(m => m.BatchId == count.BatchId && m.Kind == StockMovementKind.Adjustment && m.AtUtc > count.AtUtc);
        if (adjustedLater) return "پس از این شمارش اصلاح دیگری روی این بچ ثبت شده است؛ موجودی را از جزئیات قلم بررسی کنید.";

        var batch = await db.Batches.FirstAsync(b => b.Id == count.BatchId);
        if (batch.OnHand + difference < 0)
        {
            return $"موجودی فعلی ({batch.OnHand}) کمتر از کسری ({-difference}) است؛ برداشت‌های پس از شمارش را بررسی و با اصلاح دستی درست کنید.";
        }

        db.ExpectVersion(count, version);
        // نسخه‌ی خوانده‌شده‌ی همین درخواست: برداشت هم‌زمان ۴۰۹ می‌شود تا محاسبه روی موجودی کهنه نماند.
        db.ExpectVersion(batch, batch.Version);
        var movement = ledger.Post(batch, StockMovementKind.Adjustment, difference, count.AtUtc,
            $"انبارگردانی: شمرده‌شده {count.Counted}، مورد انتظار {count.ExpectedOnHand}", stocktakeCountId: count.Id);
        count.AdjustmentId = movement.Id;
        return null;
    }
}
