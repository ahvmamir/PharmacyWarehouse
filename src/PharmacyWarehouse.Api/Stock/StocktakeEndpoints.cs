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

    private sealed record ScopedBatch(Batch Batch, Product Product, Guid? EffectiveLocation);

    // محل مؤثر بچ: محل خودش، وگرنه محل قلم. فهرست‌ها کوچک‌اند و مثل بقیه‌ی پروژه در حافظه فیلتر می‌شوند.
    private static async Task<List<ScopedBatch>> ScopedBatchesAsync(WarehouseDbContext db, HashSet<Guid> scope, Guid? productId = null)
    {
        var rows = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where productId == null || p.Id == productId
            select new { Batch = b, Product = p }).ToListAsync();

        return rows
            .Select(r => new ScopedBatch(r.Batch, r.Product, r.Batch.LocationId ?? r.Product.LocationId))
            .Where(r => r.EffectiveLocation is { } location && scope.Contains(location))
            .ToList();
    }

    private static string? ExpiryText(Batch b) =>
        b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null;

    private static string? PathOf(IReadOnlyDictionary<Guid, string> paths, Guid? id) =>
        id is { } g && paths.TryGetValue(g, out var path) ? path : null;

    private static async Task<Dictionary<Guid, string>> UserNamesAsync(WarehouseDbContext db, IEnumerable<Guid> ids)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "—");
    }

    // آخرین شمارش هر بچ؛ شمارش‌های قبلی همان بچ جایگزین شده‌اند.
    private static List<StocktakeCount> Latest(IEnumerable<StocktakeCount> counts) => counts
        .GroupBy(c => c.BatchId)
        .Select(g => g.OrderByDescending(c => c.AtUtc).ThenByDescending(c => c.Id).First())
        .ToList();

    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var stocktakes = await db.Stocktakes.AsNoTracking()
            .OrderBy(s => s.ClosedAtUtc != null).ThenByDescending(s => s.StartedAtUtc).Take(MaxList).ToListAsync();
        var ids = stocktakes.Select(s => s.Id).ToList();
        var counts = (await db.StocktakeCounts.AsNoTracking().Where(c => ids.Contains(c.StocktakeId)).ToListAsync()).ToLookup(c => c.StocktakeId);
        var paths = LocationTree.Paths(Nodes(await db.StorageLocations.AsNoTracking().ToListAsync()));
        var names = await UserNamesAsync(db, stocktakes.Select(s => s.StartedById));

        return Results.Ok(stocktakes.Select(s =>
        {
            var latest = Latest(counts[s.Id]);
            return new StocktakeListItem(s.Id, s.LocationId, PathOf(paths, s.LocationId) ?? "—", names.GetValueOrDefault(s.StartedById, "—"),
                s.StartedAtUtc, s.ClosedAtUtc, latest.Count, latest.Count(c => c.Counted != c.ExpectedOnHand), s.Version);
        }).ToList());
    }

    private static async Task<IResult> StartAsync(StartStocktakeRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        if (locations.All(l => l.Id != request.LocationId)) return ApiProblems.Validation("LocationId", "محل را انتخاب کنید.");

        var usable = LocationTree.EffectivelyActive(locations.Select(l => (new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder), l.IsActive)));
        if (!usable.Contains(request.LocationId)) return ApiProblems.Validation("LocationId", "این محل غیرفعال است.");

        // نوبت باز روی همان محل، والد یا زیرشاخه‌ی آن یعنی یک بچ در دو نوبت شمرده می‌شود.
        var nodes = Nodes(locations);
        var scope = LocationTree.Subtree(nodes, request.LocationId);
        var open = await db.Stocktakes.AsNoTracking().Where(s => s.ClosedAtUtc == null).Select(s => s.LocationId).ToListAsync();
        if (open.Any(other => scope.Contains(other) || LocationTree.Subtree(nodes, other).Contains(request.LocationId)))
        {
            return ApiProblems.Conflict("برای این محل، والد یا زیرشاخه‌ی آن نوبت بازی هست؛ اول همان را ببندید.");
        }

        var stocktake = new Stocktake { Id = Guid.NewGuid(), LocationId = request.LocationId, StartedById = userId, StartedAtUtc = DateTime.UtcNow };
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
        var scoped = await ScopedBatchesAsync(db, LocationTree.Subtree(nodes, stocktake.LocationId));

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
        var scoped = await ScopedBatchesAsync(db, LocationTree.Subtree(nodes, stocktake.LocationId), productId);
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

        var batch = await db.Batches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BatchId);
        if (batch is null) return ApiProblems.Validation("BatchId", "بچ پیدا نشد.");

        var nodes = Nodes(await db.StorageLocations.AsNoTracking().ToListAsync());
        var productLocation = await db.Products.AsNoTracking().Where(p => p.Id == batch.ProductId).Select(p => p.LocationId).FirstAsync();
        if ((batch.LocationId ?? productLocation) is not { } location || !LocationTree.Subtree(nodes, stocktake.LocationId).Contains(location))
        {
            return ApiProblems.Validation("BatchId", "این بچ در محل این نوبت نیست.");
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
        if (stocktake.ClosedAtUtc is null) return ApiProblems.Conflict("اختلاف‌ها پس از بستن نوبت تأیید می‌شوند.");
        if (count.AdjustmentId is not null) return ApiProblems.Conflict("این اختلاف قبلاً تأیید شده است.");

        var difference = count.Counted - count.ExpectedOnHand;
        if (difference == 0) return ApiProblems.Conflict("این شمارش اختلافی ندارد.");

        var newer = await db.StocktakeCounts.AnyAsync(c => c.BatchId == count.BatchId && c.Id != count.Id && c.AtUtc >= count.AtUtc);
        if (newer) return ApiProblems.Conflict("برای این بچ شمارش تازه‌تری هست؛ این شمارش دیگر معتبر نیست.");

        // اصلاح دستی پس از شمارش احتمالاً همین اختلاف را درست کرده؛ اعمال دوباره موجودی را دو بار عوض می‌کرد.
        var adjustedLater = await db.StockMovements.AnyAsync(m => m.BatchId == count.BatchId && m.Kind == StockMovementKind.Adjustment && m.AtUtc > count.AtUtc);
        if (adjustedLater) return ApiProblems.Conflict("پس از این شمارش اصلاح دیگری روی این بچ ثبت شده است؛ موجودی را از جزئیات قلم بررسی کنید.");

        var batch = await db.Batches.FirstAsync(b => b.Id == count.BatchId);
        if (batch.OnHand + difference < 0)
        {
            return ApiProblems.Conflict($"موجودی فعلی ({batch.OnHand}) کمتر از کسری ({-difference}) است؛ برداشت‌های پس از شمارش را بررسی و با اصلاح دستی درست کنید.");
        }

        db.ExpectVersion(count, request.Version);
        // نسخه‌ی خوانده‌شده‌ی همین درخواست: برداشت هم‌زمان ۴۰۹ می‌شود تا محاسبه روی موجودی کهنه نماند.
        db.ExpectVersion(batch, batch.Version);
        var movement = ledger.Post(batch, StockMovementKind.Adjustment, difference, count.AtUtc,
            $"انبارگردانی: شمرده‌شده {count.Counted}، مورد انتظار {count.ExpectedOnHand}", stocktakeCountId: count.Id);
        count.AdjustmentId = movement.Id;

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
}
