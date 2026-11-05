using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Dashboard;

// داشبورد مدیر و تنظیم آستانه‌های انقضا (بخش ۱۰ spec). فقط خواندن است، جز PUT تنظیمات.
public static class DashboardEndpoints
{
    private const int MaxExpiryItems = 200;
    private const int MaxTopProducts = 10;
    private const int MaxCorrections = 100;

    public static void MapDashboardEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Dashboard, GetDashboardAsync).RequireAuthorization(Permissions.DashboardView);
        app.MapGet(ApiRoutes.Settings, GetSettingsAsync).RequireAuthorization(Permissions.DashboardView);
        app.MapPut(ApiRoutes.Settings, SaveSettingsAsync).RequireAuthorization(Permissions.SettingsManage);
    }

    // ---------- تنظیمات ----------

    private static async Task<(AppSetting Near, AppSetting Far)> LoadThresholdsAsync(WarehouseDbContext db)
    {
        var rows = await db.AppSettings.Where(s => s.Key == DashboardRules.NearMonthsKey || s.Key == DashboardRules.FarMonthsKey).ToListAsync();
        return (rows.Single(s => s.Key == DashboardRules.NearMonthsKey), rows.Single(s => s.Key == DashboardRules.FarMonthsKey));
    }

    private static int MonthsOf(AppSetting setting, int fallback) =>
        int.TryParse(setting.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var months) ? months : fallback;

    private static AppSettingsResponse ToResponse(AppSetting near, AppSetting far) =>
        new(MonthsOf(near, DashboardRules.DefaultNearMonths), MonthsOf(far, DashboardRules.DefaultFarMonths), near.Version, far.Version);

    private static async Task<IResult> GetSettingsAsync(WarehouseDbContext db)
    {
        var (near, far) = await LoadThresholdsAsync(db);
        return Results.Ok(ToResponse(near, far));
    }

    private static async Task<IResult> SaveSettingsAsync(SaveAppSettingsRequest request, WarehouseDbContext db)
    {
        if (DashboardRules.ValidateThresholds(request.NearMonths, request.FarMonths) is { } error)
            return ApiProblems.Validation("NearMonths", error);

        var (near, far) = await LoadThresholdsAsync(db);
        // هر دو نسخه سنجیده می‌شوند، حتی برای مقدار بی‌تغییر: کاربر هر دو را با هم دیده و فرستاده است.
        if (near.Version != request.NearVersion || far.Version != request.FarVersion) return ApiProblems.VersionConflict();

        Apply(db, near, request.NearMonths, request.NearVersion);
        Apply(db, far, request.FarMonths, request.FarVersion);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(ToResponse(near, far));
    }

    // ردیف بی‌تغییر نوشته نمی‌شود تا تاریخچه و Version بی‌دلیل جلو نروند.
    private static void Apply(WarehouseDbContext db, AppSetting setting, int months, int version)
    {
        var value = months.ToString(CultureInfo.InvariantCulture);
        if (setting.Value == value) return;
        db.ExpectVersion(setting, version);
        setting.Value = value;
    }

    // ---------- داشبورد ----------

    private static async Task<IResult> GetDashboardAsync(WarehouseDbContext db)
    {
        var now = DateTime.UtcNow;
        var today = LocalDay.Today(now);
        var (near, far) = await LoadThresholdsAsync(db);
        var thresholds = ToResponse(near, far);

        var since7 = now.AddDays(-7);
        var since30 = now.AddDays(-30);
        var movements = await ConsumptionRowsAsync(db, since30);
        var userIds = movements.Select(m => m.UserId).Distinct().ToList();
        var productIds = movements.Select(m => m.ProductId).Distinct().ToList();
        var userNames = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "—");
        var productNames = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Brand == null ? p.Name : p.Name + " — " + p.Brand);

        return Results.Ok(new DashboardResponse(
            await ExpiryAsync(db, today, thresholds.NearMonths, thresholds.FarMonths),
            await BelowMinimumAsync(db, today),
            Consumption(movements.Where(m => m.OccurredAtUtc >= since7), productNames, userNames),
            Consumption(movements, productNames, userNames),
            await UnreviewedAsync(db, since30),
            await CorrectionsAsync(db, since30),
            [])); // جدول انبارگردانی در برش ۶ می‌آید
    }

    private static async Task<DashboardExpiry> ExpiryAsync(WarehouseDbContext db, DateOnly today, int nearMonths, int farMonths)
    {
        var limit = today.AddMonths(farMonths);
        var rows = await (
            from b in db.Batches.AsNoTracking()
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where b.OnHand > 0 && b.ExpiryDate != null && b.ExpiryDate <= limit
            select new { Batch = b, p.Name, p.Brand, ProductLocation = p.LocationId }).ToListAsync();

        var locations = await db.StorageLocations.AsNoTracking().ToListAsync();
        var paths = LocationTree.Paths(locations.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder)));
        string? PathOf(Guid? id) => id is { } g && paths.TryGetValue(g, out var path) ? path : null;

        DashboardExpiryBucket Bucket(ExpiryBucket bucket)
        {
            var matching = rows.Where(r => DashboardRules.Classify(r.Batch.ExpiryDate, today, nearMonths, farMonths) == bucket)
                .OrderBy(r => r.Batch.ExpiryDate).ThenBy(r => r.Batch.CreatedAtUtc).ThenBy(r => r.Batch.Id).ToList();
            var items = matching.Take(MaxExpiryItems).Select(r =>
            {
                var b = r.Batch;
                var date = b.ExpiryDate!.Value;
                return new DashboardExpiryItem(b.Id, b.ProductId, r.Name, r.Brand, b.BatchNumber, date,
                    ExpiryDate.Display(new ExpiryValue(date, b.ExpiryPrecision, b.ExpiryCalendar)), PathOf(b.LocationId) ?? PathOf(r.ProductLocation), b.OnHand);
            }).ToList();
            return new DashboardExpiryBucket(matching.Count, items);
        }

        return new DashboardExpiry(nearMonths, farMonths, Bucket(ExpiryBucket.Expired), Bucket(ExpiryBucket.Near), Bucket(ExpiryBucket.Far));
    }

    private static async Task<List<DashboardBelowMinItem>> BelowMinimumAsync(WarehouseDbContext db, DateOnly today)
    {
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive && p.MinStock != null).ToListAsync();
        // بچ بی‌تاریخ منقضی نیست؛ همان قاعده‌ی ExpiryDate.IsExpired.
        var unexpired = await db.Batches.AsNoTracking()
            .Where(b => b.OnHand > 0 && (b.ExpiryDate == null || b.ExpiryDate >= today))
            .GroupBy(b => b.ProductId).Select(g => new { ProductId = g.Key, OnHand = g.Sum(b => b.OnHand) })
            .ToDictionaryAsync(g => g.ProductId, g => g.OnHand);

        return products
            .Select(p => new DashboardBelowMinItem(p.Id, p.Name, p.Brand, p.MinStock!.Value, unexpired.GetValueOrDefault(p.Id)))
            .Where(i => DashboardRules.IsBelowMinimum(i.MinStock, i.UnexpiredOnHand))
            .OrderBy(i => i.UnexpiredOnHand - i.MinStock).ThenBy(i => i.ProductName, StringComparer.Ordinal).ThenBy(i => i.ProductId).ToList();
    }

    private sealed record ConsumptionRow(Guid ProductId, Guid UserId, DateTime OccurredAtUtc, int Quantity);

    // زمان از خود برداشت است، نه از حرکت برگشت؛ وگرنه رد امروزِ برداشت قدیمی، مصرف بازه‌ی کوتاه را منفی می‌کرد.
    private static Task<List<ConsumptionRow>> ConsumptionRowsAsync(WarehouseDbContext db, DateTime since) =>
        (from m in db.StockMovements.AsNoTracking()
         join w in db.Withdrawals.AsNoTracking() on m.WithdrawalId equals w.Id
         where (m.Kind == StockMovementKind.Withdrawal || m.Kind == StockMovementKind.Reversal) && w.OccurredAtUtc >= since
         select new ConsumptionRow(w.ProductId, w.UserId, w.OccurredAtUtc, -m.Quantity)).ToListAsync();

    private static DashboardConsumption Consumption(IEnumerable<ConsumptionRow> rows, Dictionary<Guid, string> products, Dictionary<Guid, string> users)
    {
        var list = rows as IReadOnlyCollection<ConsumptionRow> ?? rows.ToList();
        List<DashboardConsumptionItem> Totals(Func<ConsumptionRow, Guid> key, Dictionary<Guid, string> names) => list
            .GroupBy(key)
            .Select(g => new DashboardConsumptionItem(g.Key, names.GetValueOrDefault(g.Key, "—"), g.Sum(r => r.Quantity)))
            .Where(i => i.Quantity > 0)
            .OrderByDescending(i => i.Quantity).ThenBy(i => i.Name, StringComparer.Ordinal).ThenBy(i => i.Id).ToList();

        return new DashboardConsumption(Totals(r => r.ProductId, products).Take(MaxTopProducts).ToList(), Totals(r => r.UserId, users));
    }

    private static async Task<DashboardUnreviewed> UnreviewedAsync(WarehouseDbContext db, DateTime since)
    {
        var pending = db.Withdrawals.AsNoTracking().Where(w => w.Status == WithdrawalStatus.Pending);
        var count = await pending.CountAsync();
        var oldest = count == 0 ? (DateTime?)null : await pending.OrderBy(w => w.AtUtc).Select(w => w.AtUtc).FirstAsync();
        var recent = db.Withdrawals.AsNoTracking().Where(w => w.AtUtc >= since);

        return new DashboardUnreviewed(count, oldest,
            await db.Withdrawals.AsNoTracking().CountAsync(w => w.SelfReviewed && w.ReviewedAtUtc >= since),
            await recent.CountAsync(w => w.IsLate),
            await recent.CountAsync(w => w.AfterStocktake));
    }

    private static async Task<List<DashboardCorrectionItem>> CorrectionsAsync(WarehouseDbContext db, DateTime since)
    {
        var rows = await (
            from m in db.StockMovements.AsNoTracking()
            join b in db.Batches.AsNoTracking() on m.BatchId equals b.Id
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where (m.Kind == StockMovementKind.Adjustment || m.Kind == StockMovementKind.Disposal) && m.AtUtc >= since
            orderby m.AtUtc descending, m.Id descending
            select new { m.Id, m.Kind, m.AtUtc, p.Name, b.BatchNumber, m.Quantity, m.UserId, m.Note }).Take(MaxCorrections).ToListAsync();

        var userIds = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "—");

        return rows.Select(r => new DashboardCorrectionItem(r.Id, r.Kind, r.AtUtc, r.Name, r.BatchNumber, r.Quantity,
            r.UserId is { } id ? names.GetValueOrDefault(id, "—") : null, r.Note)).ToList();
    }
}
