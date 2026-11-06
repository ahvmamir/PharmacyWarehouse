using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Dashboard;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;

namespace PharmacyWarehouse.Api.Reports;

// گزارش حرکت و مصرف. بازه با تعداد روز می‌آید و سرور حسابش می‌کند، چون منطقه‌ی زمانی مرورگر قابل‌اتکا نیست (بخش ۷.۵ spec).
public static class ReportEndpoints
{
    private const int MaxDays = 365;
    private const int MaxTake = 200;

    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.ReportMovements, MovementsAsync).RequireAuthorization(Permissions.ReportsView);
        app.MapGet(ApiRoutes.ReportConsumption, ConsumptionAsync).RequireAuthorization(Permissions.ReportsView);
    }

    private static async Task<IResult> MovementsAsync(WarehouseDbContext db, int days = 30, string? kind = null, Guid? productId = null, bool oldestFirst = false, int skip = 0, int take = 100)
    {
        if (days is < 1 or > MaxDays) return ApiProblems.Validation("days", $"بازه باید ۱ تا {MaxDays} روز باشد.");
        if (skip < 0) return ApiProblems.Validation("skip", "مقدار نامعتبر است.");

        StockMovementKind? parsedKind = null;
        if (!string.IsNullOrEmpty(kind))
        {
            // عدد هم با Enum.TryParse پذیرفته می‌شود؛ فقط نام مجاز است.
            if (!Enum.GetNames<StockMovementKind>().Contains(kind)) return ApiProblems.Validation("kind", "نوع حرکت نامعتبر است.");
            parsedKind = Enum.Parse<StockMovementKind>(kind);
        }

        take = Math.Clamp(take, 1, MaxTake);
        var since = DateTime.UtcNow.AddDays(-days);

        var query =
            from m in db.StockMovements.AsNoTracking()
            join b in db.Batches.AsNoTracking() on m.BatchId equals b.Id
            join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
            where m.OccurredAtUtc >= since
                  && (parsedKind == null || m.Kind == parsedKind)
                  && (productId == null || p.Id == productId)
            select new { m, b.BatchNumber, p.Name, p.Brand, ProductId = p.Id };

        var total = await query.CountAsync();
        var ordered = oldestFirst
            ? query.OrderBy(r => r.m.OccurredAtUtc).ThenBy(r => r.m.Id)
            : query.OrderByDescending(r => r.m.OccurredAtUtc).ThenByDescending(r => r.m.Id);
        var rows = await ordered.Skip(skip).Take(take).ToListAsync();

        // موجودی بعد از حرکت فقط با فیلتر قلم؛ از همه‌ی حرکت‌های قلم حساب می‌شود (نه فقط بازه و صفحه‌ی فعلی).
        IReadOnlyDictionary<Guid, int>? productBalances = null;
        if (productId is { } product)
        {
            var all = await (
                from m in db.StockMovements.AsNoTracking()
                join b in db.Batches.AsNoTracking() on m.BatchId equals b.Id
                where b.ProductId == product
                select new { m.Id, m.BatchId, m.AtUtc, m.Quantity }).ToListAsync();
            productBalances = MovementBalance.After(all.Select(x => new MovementBalance.Row(x.Id, x.BatchId, x.AtUtc, x.Quantity))).Product;
        }

        var userIds = rows.Where(r => r.m.UserId != null).Select(r => r.m.UserId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "—");

        return Results.Ok(new MovementReportResponse(total, rows.Select(r => new MovementReportItem(
            r.m.Id, r.m.AtUtc, r.m.OccurredAtUtc, r.m.Kind, r.ProductId, r.Name, r.Brand, r.BatchNumber, r.m.Quantity,
            r.m.UserId is { } id ? names.GetValueOrDefault(id, "—") : "سیستم", r.m.Note,
            productBalances is null ? null : productBalances[r.m.Id])).ToList()));
    }

    private static async Task<IResult> ConsumptionAsync(WarehouseDbContext db, int days = 30)
    {
        if (days is < 1 or > MaxDays) return ApiProblems.Validation("days", $"بازه باید ۱ تا {MaxDays} روز باشد.");

        return Results.Ok(new ConsumptionReportResponse(days, await DashboardEndpoints.ConsumptionSinceAsync(db, DateTime.UtcNow.AddDays(-days))));
    }
}
