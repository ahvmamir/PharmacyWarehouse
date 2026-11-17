using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// دوره‌ی انبارگردانی (بخش ۱۵.۳ spec): دامنه (محل‌ها و دسته‌ها)، آمار همه‌ی بچ‌های دامنه با آخرین شمارش در کل دوره،
// رفع اختلاف در سطح دوره (اصلاح یا صرف‌نظر) و بستن دوره. مجوز همه stock.adjust است، مثل نوبت‌ها.
public static class StocktakePeriodEndpoints
{
    private const string Overlap = "دوره‌ی باز دیگری همین محل‌ها و دسته‌ها را پوشش می‌دهد؛ اول آن را ببندید یا دامنه را تغییر دهید.";

    public static void MapStocktakePeriodEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.StocktakePeriods).RequireAuthorization(Permissions.StockAdjust);
        group.MapGet("", ListAsync);
        group.MapPost("", CreateAsync);
        group.MapGet("{id:guid}", GetAsync);
        group.MapPut("{id:guid}", UpdateAsync);
        group.MapPost("{id:guid}/close", CloseAsync);
        group.MapPost("{id:guid}/resolve", ResolveAsync);
    }

    private sealed record Scope(List<Guid> Locations, List<Guid> Categories);

    private static async Task<Scope> ScopeOfAsync(WarehouseDbContext db, Guid periodId) => new(
        await db.StocktakePeriodLocations.AsNoTracking().Where(l => l.PeriodId == periodId).Select(l => l.LocationId).ToListAsync(),
        await db.StocktakePeriodCategories.AsNoTracking().Where(c => c.PeriodId == periodId).Select(c => c.CategoryId).ToListAsync());

    private static List<LocationNode> Nodes(IEnumerable<StorageLocation> locations) =>
        locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)).ToList();

    private static string ScopeText(Scope scope, IReadOnlyDictionary<Guid, string> paths, IReadOnlyDictionary<Guid, string> categories)
    {
        static string Join(IEnumerable<string> items)
        {
            var list = items.ToList();
            return list.Count <= 3 ? string.Join("، ", list) : string.Join("، ", list.Take(3)) + $" و {list.Count - 3} مورد دیگر";
        }

        var where = scope.Locations.Count == 0 ? "همه‌ی محل‌ها" : Join(scope.Locations.Select(l => paths.GetValueOrDefault(l, "محل حذف‌شده")));
        var what = scope.Categories.Count == 0 ? "همه‌ی دسته‌ها" : Join(scope.Categories.Select(c => categories.GetValueOrDefault(c, "دسته‌ی حذف‌شده")));
        return $"{where} — {what}";
    }

    // ---------- آمار ----------

    private sealed record Stats(List<PeriodRowItem> Rows, PeriodSummary Summary);

    private static async Task<Stats> StatsAsync(WarehouseDbContext db, Guid periodId, Scope scope, List<StorageLocation> locations)
    {
        var nodes = Nodes(locations);
        var paths = LocationTree.Paths(nodes);
        HashSet<Guid>? locationScope = scope.Locations.Count == 0 ? null : scope.Locations.SelectMany(l => LocationTree.Subtree(nodes, l)).ToHashSet();
        var scoped = await StocktakeEndpoints.ScopedBatchesAsync(db, locationScope, scope.Categories);

        var sessions = await db.Stocktakes.AsNoTracking().Where(s => s.PeriodId == periodId).ToDictionaryAsync(s => s.Id);
        var sessionIds = sessions.Keys.ToList();
        var counts = await db.StocktakeCounts.AsNoTracking().Where(c => sessionIds.Contains(c.StocktakeId)).ToListAsync();
        var latest = StocktakeEndpoints.Latest(counts).ToDictionary(c => c.BatchId);
        var byBatch = counts.ToLookup(c => c.BatchId);

        // بچ شمرده‌شده‌ای که حالا بیرون از دامنه است (جابه‌جا شده) هم در آمار می‌ماند.
        var inScope = scoped.ToDictionary(s => s.Batch.Id);
        var missingIds = latest.Keys.Where(id => !inScope.ContainsKey(id)).ToList();
        var extra = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where missingIds.Contains(b.Id)
            select new { Batch = b, Product = p }).ToListAsync();
        var all = scoped.Where(s => s.Batch.OnHand > 0 || latest.ContainsKey(s.Batch.Id))
            .Concat(extra.Select(e => new StocktakeEndpoints.ScopedBatch(e.Batch, e.Product, e.Batch.LocationId ?? e.Product.LocationId)))
            .ToList();

        var users = await StocktakeEndpoints.UserNamesAsync(db, latest.Values.Select(c => c.UserId));
        var misplaced = await db.MisplacedCounts.AsNoTracking().Where(f => sessionIds.Contains(f.StocktakeId)).ToListAsync();
        var lastFound = misplaced.GroupBy(f => f.BatchId).ToDictionary(g => g.Key, g => g.Max(f => f.AtUtc));
        var today = LocalDay.Today(DateTime.UtcNow);
        var rows = all.Select(s =>
        {
            latest.TryGetValue(s.Batch.Id, out var c);
            var needsRecount = c is not null && c.AdjustmentId is null && c.DismissedAtUtc is null
                && lastFound.TryGetValue(s.Batch.Id, out var found) && found > c.AtUtc;
            var session = c is null ? null : sessions[c.StocktakeId];
            var difference = c is null ? 0 : c.Counted - c.ExpectedOnHand;
            var status = StocktakeRules.Status(c is not null, session?.ClosedAtUtc is not null, difference, c?.AdjustmentId is not null, c?.DismissedAtUtc is not null);
            // ناهمخوان: آخرین شمارش هر نوبت برای این بچ، در نوبت‌های مختلف عددهای متفاوت.
            var conflicting = byBatch[s.Batch.Id]
                .GroupBy(x => x.StocktakeId)
                .Select(g => g.OrderByDescending(x => x.AtUtc).First().Counted)
                .Distinct().Count() > 1;
            return new PeriodRowItem(s.Batch.Id, s.Product.Id, s.Product.Name, s.Product.Brand, s.Batch.BatchNumber, StocktakeEndpoints.ExpiryText(s.Batch),
                s.EffectiveLocation is { } l && paths.TryGetValue(l, out var path) ? path : null, s.Batch.OnHand,
                status, c?.Id, c?.Version, c?.StocktakeId, c?.ExpectedOnHand, c?.Counted, c is null ? null : difference,
                c?.AtUtc, c is null ? null : users.GetValueOrDefault(c.UserId, "—"), conflicting, c?.DismissNote,
                needsRecount, StocktakeEndpoints.IsExpired(s.Batch, today));
        })
        .OrderBy(r => r.Status switch
        {
            PeriodRowStatus.Discrepancy => 0, PeriodRowStatus.Pending => 1, PeriodRowStatus.NotCounted => 2,
            PeriodRowStatus.Matched => 3, PeriodRowStatus.Adjusted => 4, _ => 5
        })
        .ThenBy(r => r.ProductName, StringComparer.Ordinal).ThenBy(r => r.BatchNumber, StringComparer.Ordinal)
        .ToList();

        int Count(PeriodRowStatus status) => rows.Count(r => r.Status == status);
        return new Stats(rows, new PeriodSummary(Count(PeriodRowStatus.NotCounted), Count(PeriodRowStatus.Pending), Count(PeriodRowStatus.Matched),
            Count(PeriodRowStatus.Discrepancy), Count(PeriodRowStatus.Adjusted), Count(PeriodRowStatus.Dismissed),
            rows.Count(r => r.NeedsRecount), misplaced.Count(f => f.ResolvedAtUtc is null)));
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db)
    {
        var periods = await db.StocktakePeriods.AsNoTracking().OrderBy(p => p.ClosedAtUtc != null).ThenByDescending(p => p.StartedAtUtc).Take(100).ToListAsync();
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(Nodes(locations));
        var categories = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
        var sessions = (await db.Stocktakes.AsNoTracking().Select(s => new { s.PeriodId, s.ClosedAtUtc }).ToListAsync()).ToLookup(s => s.PeriodId);

        var items = new List<PeriodListItem>();
        foreach (var p in periods)
        {
            var scope = await ScopeOfAsync(db, p.Id);
            // تعداد اختلاف فقط برای دوره‌ی باز لازم است؛ دوره‌ی بسته اختلاف باز ندارد.
            var discrepancies = p.ClosedAtUtc is null ? (await StatsAsync(db, p.Id, scope, locations)).Summary.Discrepancy : 0;
            items.Add(new PeriodListItem(p.Id, p.Name, p.ClosedAtUtc is null, p.StartedAtUtc, p.ClosedAtUtc, ScopeText(scope, paths, categories),
                sessions[p.Id].Count(), sessions[p.Id].Count(s => s.ClosedAtUtc is null), discrepancies, p.Version));
        }

        return Results.Ok(items);
    }

    private static async Task<IResult> GetAsync(Guid id, WarehouseDbContext db)
    {
        var period = await db.StocktakePeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        return period is null ? ApiProblems.NotFound() : Results.Ok(await ToDetailAsync(db, period));
    }

    private static async Task<PeriodDetail> ToDetailAsync(WarehouseDbContext db, StocktakePeriod period)
    {
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(Nodes(locations));
        var categories = await db.ProductCategories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name);
        var scope = await ScopeOfAsync(db, period.Id);
        var stats = await StatsAsync(db, period.Id, scope, locations);
        var names = await StocktakeEndpoints.UserNamesAsync(db, [period.StartedById]);

        return new PeriodDetail
        {
            Id = period.Id,
            Name = period.Name,
            IsOpen = period.ClosedAtUtc is null,
            IsLegacy = period.Id == StocktakePeriod.LegacyId,
            StartedAtUtc = period.StartedAtUtc,
            ClosedAtUtc = period.ClosedAtUtc,
            StartedByName = names.GetValueOrDefault(period.StartedById, "—"),
            Version = period.Version,
            LocationIds = scope.Locations,
            LocationPaths = scope.Locations.Select(l => paths.GetValueOrDefault(l, "محل حذف‌شده")).OrderBy(p => p, StringComparer.Ordinal).ToList(),
            CategoryIds = scope.Categories,
            CategoryNames = scope.Categories.Select(c => categories.GetValueOrDefault(c, "دسته‌ی حذف‌شده")).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            Sessions = await StocktakeEndpoints.ListItemsAsync(db, period.Id),
            Rows = stats.Rows,
            Summary = stats.Summary,
            Misplaced = await StocktakeEndpoints.MisplacedItemsAsync(
                db, await db.Stocktakes.AsNoTracking().Where(s => s.PeriodId == period.Id).Select(s => s.Id).ToListAsync(), paths)
        };
    }

    // ---------- ساخت و ویرایش دامنه ----------

    private static async Task<(string? Name, IResult? Problem)> ValidateAsync(WarehouseDbContext db, SavePeriodRequest request, List<StorageLocation> locations)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (name is null) return (null, ApiProblems.Validation("Name", "نام دوره را وارد کنید؛ مثلاً «پاییز ۱۴۰۵»."));
        if (name.Length > StocktakeRules.MaxPeriodName) return (null, ApiProblems.Validation("Name", $"نام دوره حداکثر {StocktakeRules.MaxPeriodName} نویسه است."));
        if (request.LocationIds.Distinct().Count() != request.LocationIds.Count) return (null, ApiProblems.Validation("LocationIds", "یک محل دو بار آمده است."));
        if (request.CategoryIds.Distinct().Count() != request.CategoryIds.Count) return (null, ApiProblems.Validation("CategoryIds", "یک دسته دو بار آمده است."));
        if (request.LocationIds.Any(id => locations.All(l => l.Id != id))) return (null, ApiProblems.Validation("LocationIds", "محل پیدا نشد."));

        var categoryIds = request.CategoryIds;
        var known = await db.ProductCategories.AsNoTracking().CountAsync(c => categoryIds.Contains(c.Id));
        if (known != categoryIds.Count) return (null, ApiProblems.Validation("CategoryIds", "دسته پیدا نشد."));
        return (name, null);
    }

    private static async Task<bool> OverlapsAsync(WarehouseDbContext db, SavePeriodRequest request, List<StorageLocation> locations, Guid? exceptId)
    {
        var nodes = Nodes(locations);
        var openIds = await db.StocktakePeriods.AsNoTracking().Where(p => p.ClosedAtUtc == null && p.Id != exceptId).Select(p => p.Id).ToListAsync();
        foreach (var other in openIds)
        {
            var scope = await ScopeOfAsync(db, other);
            if (StocktakeRules.ScopesOverlap(request.LocationIds, request.CategoryIds, scope.Locations, scope.Categories, id => LocationTree.Subtree(nodes, id)))
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<IResult> CreateAsync(SavePeriodRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var (name, problem) = await ValidateAsync(db, request, locations);
        if (problem is not null) return problem;
        if (await OverlapsAsync(db, request, locations, null)) return ApiProblems.Conflict(Overlap);

        var period = new StocktakePeriod { Id = Guid.NewGuid(), Name = name!, StartedById = userId, StartedAtUtc = DateTime.UtcNow };
        db.StocktakePeriods.Add(period);
        foreach (var location in request.LocationIds) db.StocktakePeriodLocations.Add(new StocktakePeriodLocation { Id = Guid.NewGuid(), PeriodId = period.Id, LocationId = location });
        foreach (var category in request.CategoryIds) db.StocktakePeriodCategories.Add(new StocktakePeriodCategory { Id = Guid.NewGuid(), PeriodId = period.Id, CategoryId = category });
        await db.SaveChangesAsync();
        return Results.Ok(await ToDetailAsync(db, period));
    }

    // افزودن به دامنه آزاد است؛ برداشتن محل یا دسته‌ای که نوبتی از این دوره رویش هست ۴۰۹ می‌دهد.
    private static async Task<IResult> UpdateAsync(Guid id, SavePeriodRequest request, WarehouseDbContext db)
    {
        var period = await db.StocktakePeriods.FirstOrDefaultAsync(p => p.Id == id);
        if (period is null) return ApiProblems.NotFound();
        if (period.ClosedAtUtc is not null) return ApiProblems.Conflict("دوره‌ی بسته ویرایش نمی‌شود.");
        if (period.Version != request.Version) return ApiProblems.VersionConflict();

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var (name, problem) = await ValidateAsync(db, request, locations);
        if (problem is not null) return problem;
        if (await OverlapsAsync(db, request, locations, id)) return ApiProblems.Conflict(Overlap);

        var nodes = Nodes(locations);
        var paths = LocationTree.Paths(nodes);
        var sessions = await db.Stocktakes.AsNoTracking().Where(s => s.PeriodId == id).ToListAsync();
        var outside = sessions.Where(s => !StocktakeRules.LocationInPeriod(s.LocationId, request.LocationIds, l => LocationTree.Subtree(nodes, l))).ToList();
        if (outside.Count > 0)
        {
            return ApiProblems.Conflict($"نوبت‌های این دوره روی این محل‌ها هستند و از دامنه برداشته نمی‌شوند: {string.Join("، ", outside.Select(s => paths.GetValueOrDefault(s.LocationId, "—")).Distinct())}");
        }

        if (request.CategoryIds.Count > 0 && sessions.Any(s => s.CategoryId is not { } c || !request.CategoryIds.Contains(c)) && sessions.Count > 0)
        {
            return ApiProblems.Conflict("نوبت‌های این دوره دسته‌هایی بیرون از دامنه‌ی تازه دارند (یا همه‌ی دسته‌ها را شمرده‌اند)؛ دسته‌ها را این‌طور محدود نمی‌شود کرد.");
        }

        period.Name = name!;
        db.ExpectVersion(period, request.Version);

        var currentLocations = await db.StocktakePeriodLocations.Where(l => l.PeriodId == id).ToListAsync();
        db.StocktakePeriodLocations.RemoveRange(currentLocations.Where(l => !request.LocationIds.Contains(l.LocationId)));
        foreach (var location in request.LocationIds.Where(l => currentLocations.All(c => c.LocationId != l)))
            db.StocktakePeriodLocations.Add(new StocktakePeriodLocation { Id = Guid.NewGuid(), PeriodId = id, LocationId = location });

        var currentCategories = await db.StocktakePeriodCategories.Where(c => c.PeriodId == id).ToListAsync();
        db.StocktakePeriodCategories.RemoveRange(currentCategories.Where(c => !request.CategoryIds.Contains(c.CategoryId)));
        foreach (var category in request.CategoryIds.Where(c => currentCategories.All(x => x.CategoryId != c)))
            db.StocktakePeriodCategories.Add(new StocktakePeriodCategory { Id = Guid.NewGuid(), PeriodId = id, CategoryId = category });

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(await ToDetailAsync(db, period));
    }

    // ---------- بستن و رفع اختلاف ----------

    private static async Task<IResult> CloseAsync(Guid id, VersionRequest request, WarehouseDbContext db, AuditContext audit)
    {
        var period = await db.StocktakePeriods.FirstOrDefaultAsync(p => p.Id == id);
        if (period is null) return ApiProblems.NotFound();
        if (period.ClosedAtUtc is not null) return ApiProblems.Conflict("این دوره قبلاً بسته شده است.");
        if (period.Version != request.Version) return ApiProblems.VersionConflict();

        var open = await db.Stocktakes.CountAsync(s => s.PeriodId == id && s.ClosedAtUtc == null);
        if (open > 0) return ApiProblems.Conflict($"{open} نوبت این دوره هنوز باز است؛ اول نوبت‌ها را ببندید.");

        var stats = await StatsAsync(db, id, await ScopeOfAsync(db, id), await db.StorageLocations.AsNoTracking().ToListAsync());
        // شمارش دوباره اول گفته می‌شود: اختلاف چنین بچی نه اصلاح می‌شود نه صرف‌نظر.
        if (stats.Summary.OpenMisplaced > 0) return ApiProblems.Conflict($"{stats.Summary.OpenMisplaced} مورد «جای نادرست» هنوز رفع نشده است؛ جعبه‌ها را برگردانید یا محل بچ را عوض کنید.");
        if (stats.Summary.Recount > 0) return ApiProblems.Conflict($"{stats.Summary.Recount} بچ پس از پیدا شدن جعبه‌هایش در جای دیگر، نیاز به شمارش دوباره دارد.");
        if (stats.Summary.Discrepancy > 0) return ApiProblems.Conflict($"{stats.Summary.Discrepancy} اختلاف هنوز رفع نشده است؛ هر کدام را اصلاح کنید یا با یادداشت صرف‌نظر کنید.");

        db.ExpectVersion(period, request.Version);
        period.ClosedAtUtc = DateTime.UtcNow;
        period.ClosedById = audit.UserId;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(await ToDetailAsync(db, period));
    }

    private static async Task<IResult> ResolveAsync(Guid id, ResolveCountsRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");
        if (request.Items is null || request.Items.Count == 0) return ApiProblems.Validation("Items", "حداقل یک اختلاف انتخاب کنید.");
        if (request.Items.Count > ResolveCountsRequest.MaxItems) return ApiProblems.Validation("Items", $"حداکثر {ResolveCountsRequest.MaxItems} مورد در هر بار.");
        if (request.Items.Select(i => i.CountId).Distinct().Count() != request.Items.Count) return ApiProblems.Validation("Items", "یک شمارش فقط یک بار می‌آید.");

        var period = await db.StocktakePeriods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (period is null) return ApiProblems.NotFound();
        if (period.ClosedAtUtc is not null) return ApiProblems.Conflict("دوره‌ی بسته تغییر نمی‌کند.");

        var done = new List<Guid>();
        var failed = new List<BulkFailure>();
        foreach (var item in request.Items)
        {
            // هر مورد جدا ذخیره می‌شود؛ شکست یکی بقیه را خراب نمی‌کند (مثل تأیید گروهی برداشت).
            db.ChangeTracker.Clear();
            var count = await db.StocktakeCounts.FirstOrDefaultAsync(c => c.Id == item.CountId);
            var stocktake = count is null ? null : await db.Stocktakes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == count.StocktakeId && s.PeriodId == id);
            if (count is null || stocktake is null) { failed.Add(new BulkFailure(item.CountId, "شمارش در این دوره پیدا نشد.")); continue; }
            if (count.Version != item.Version) { failed.Add(new BulkFailure(item.CountId, "این شمارش در این فاصله تغییر کرده است؛ صفحه را تازه کنید.")); continue; }

            var problem = item.Dismiss
                ? await PrepareDismissAsync(db, stocktake, count, item, userId)
                : await StocktakeEndpoints.PrepareAdjustmentAsync(db, ledger, stocktake, count, item.Version);
            if (problem is not null) { failed.Add(new BulkFailure(item.CountId, problem)); continue; }

            try
            {
                await db.SaveChangesAsync();
                done.Add(item.CountId);
            }
            catch (DbUpdateConcurrencyException)
            {
                failed.Add(new BulkFailure(item.CountId, "موجودی یا شمارش هم‌زمان تغییر کرد؛ صفحه را تازه کنید."));
            }
            catch (DbUpdateException)
            {
                failed.Add(new BulkFailure(item.CountId, "ثبت انجام نشد؛ کمی بعد دوباره تلاش کنید."));
            }
        }

        db.ChangeTracker.Clear();
        return Results.Ok(new ResolveCountsResult(done, failed));
    }

    // صرف‌نظر: موجودی عوض نمی‌شود؛ فقط اختلاف «رفع‌شده» علامت می‌خورد و دلیلش در تاریخچه می‌ماند.
    private static async Task<string?> PrepareDismissAsync(WarehouseDbContext db, Stocktake stocktake, StocktakeCount count, ResolveCountItem item, Guid userId)
    {
        var note = TextNormalizer.Clean(item.Note);
        if (note is null) return "برای صرف‌نظر یادداشت لازم است؛ مثلاً «خطای شمارش، دوباره شمرده شد».";
        if (note.Length > ProductRules.MaxNote) return $"یادداشت حداکثر {ProductRules.MaxNote} نویسه است.";
        if (stocktake.ClosedAtUtc is null) return "اختلاف‌ها پس از بستن نوبت رفع می‌شوند.";
        if (count.AdjustmentId is not null) return "این اختلاف قبلاً اصلاح شده است.";
        if (count.DismissedAtUtc is not null) return "از این اختلاف قبلاً صرف‌نظر شده است.";
        if (count.Counted == count.ExpectedOnHand) return "این شمارش اختلافی ندارد.";
        if (await db.StocktakeCounts.AnyAsync(c => c.BatchId == count.BatchId && c.Id != count.Id && c.AtUtc >= count.AtUtc))
        {
            return "برای این بچ شمارش تازه‌تری هست؛ این شمارش دیگر معتبر نیست.";
        }

        if (await StocktakeEndpoints.NeedsRecountAsync(db, stocktake.PeriodId, count)) return StocktakeEndpoints.NeedsRecountMessage;

        db.ExpectVersion(count, item.Version);
        count.DismissedAtUtc = DateTime.UtcNow;
        count.DismissedById = userId;
        count.DismissNote = note;
        return null;
    }
}
