using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Storage;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Stock;

// برداشت (بخش ۷.۱ و ۷.۵ spec): در یک SaveChanges، حرکت‌ها، OnHand بچ‌ها و Withdrawal ثبت می‌شوند.
public static class WithdrawalEndpoints
{
    private const int MaxListTake = 200;
    private const int MaxMinutesAgo = WithdrawalRules.MaxBackdateDays * 24 * 60;

    public static void MapWithdrawalEndpoints(this WebApplication app)
    {
        app.MapGet($"{ApiRoutes.Products}/{{id:guid}}/allocation", AllocationAsync).RequireAuthorization(Permissions.WithdrawalCreate);

        var group = app.MapGroup(ApiRoutes.Withdrawals).RequireAuthorization(Permissions.WithdrawalCreate);
        group.MapPost("", CreateAsync);
        group.MapGet("", ListMineAsync);
    }

    private static async Task<Dictionary<Guid, string>> PathsAsync(WarehouseDbContext db)
    {
        var all = await db.StorageLocations.AsNoTracking().ToListAsync();
        return LocationTree.Paths(all.Select(l => new LocationNode(l.Id, l.ParentId, l.Name, l.SortOrder))).ToDictionary(p => p.Key, p => p.Value);
    }

    private static async Task<IResult> AllocationAsync(Guid id, int quantity, WarehouseDbContext db, int minutesAgo = 0)
    {
        if (quantity <= 0) return ApiProblems.Validation("Quantity", "تعداد باید بیشتر از صفر باشد.");
        if (minutesAgo < 0 || minutesAgo > MaxMinutesAgo) return ApiProblems.Validation("MinutesAgo", "زمان برداشت نامعتبر است.");

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return ApiProblems.NotFound();
        if (!product.IsActive) return ApiProblems.Validation("ProductId", "این قلم غیرفعال است.");

        // «منقضی» نسبت به روز برداشت فیزیکی، مثل ثبت.
        var today = LocalDay.Today(DateTime.UtcNow.AddMinutes(-minutesAgo));
        var batches = await db.Batches.AsNoTracking().Where(b => b.ProductId == id && b.OnHand > 0).ToListAsync();
        var paths = await PathsAsync(db);
        var byId = batches.ToDictionary(b => b.Id);

        var result = BatchAllocator.Allocate(batches.Select(ToAllocatable), quantity, today);

        AllocationLineItem Item(Batch b, int q) => new(
            b.Id, b.BatchNumber,
            b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null,
            (b.LocationId ?? product.LocationId) is { } loc && paths.TryGetValue(loc, out var path) ? path : null,
            q, b.OnHand, ExpiryDate.IsExpired(b.ExpiryDate, today), b.Version);

        var lines = result.Lines.Select(l => Item(byId[l.BatchId], l.Quantity)).ToList();
        var fefo = BatchAllocator.FefoOrder(batches.Select(ToAllocatable), today);
        var suggested = fefo.Select(f => f.Id).ToHashSet();
        var available = fefo.Select(f => Item(byId[f.Id], 0))
            .Concat(batches.Where(b => !suggested.Contains(b.Id)).OrderBy(b => b.ExpiryDate).ThenBy(b => b.CreatedAtUtc).ThenBy(b => b.Id).Select(b => Item(b, 0)))
            .ToList();

        return Results.Ok(new AllocationResponse(product.Id, product.Name, product.Brand, product.PackDescription, product.AllowExpiredWithdrawal,
            lines, available, result.Shortfall, fefo.Sum(f => f.OnHand)));
    }

    private static AllocatableBatch ToAllocatable(Batch b) => new(b.Id, b.BatchNumber, b.ExpiryDate, b.CreatedAtUtc, b.OnHand);

    private static async Task<IResult> CreateAsync(WithdrawalRequest request, WarehouseDbContext db, StockLedger ledger, AuditContext audit)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        if (request.Lines is null || request.Lines.Count == 0) return ApiProblems.Validation("Lines", "حداقل یک بچ لازم است.");
        if (request.Lines.Any(l => l.Quantity <= 0)) return ApiProblems.Validation("Quantity", "تعداد هر بچ باید بیشتر از صفر باشد.");
        if (request.Lines.Select(l => l.BatchId).Distinct().Count() != request.Lines.Count) return ApiProblems.Validation("Lines", "یک بچ فقط یک بار می‌آید.");

        var note = TextNormalizer.Clean(request.Note);
        if (note is { Length: > ProductRules.MaxNote }) return ApiProblems.Validation("Note", $"یادداشت حداکثر {ProductRules.MaxNote} نویسه است.");

        var now = DateTime.UtcNow;
        if (request.MinutesAgo is < 0 or > MaxMinutesAgo) return ApiProblems.Validation("MinutesAgo", "زمان برداشت نامعتبر است.");
        var requestedTime = request.MinutesAgo is { } minutes ? now.AddMinutes(-minutes) : request.OccurredAtUtc;
        var time = WithdrawalRules.ResolveTime(requestedTime, now);
        if (time.Error is { } timeError) return ApiProblems.Validation("OccurredAtUtc", timeError);

        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId);
        if (product is null) return ApiProblems.NotFound();
        if (!product.IsActive) return ApiProblems.Validation("ProductId", "این قلم غیرفعال است.");

        var ids = request.Lines.Select(l => l.BatchId).ToList();
        var batches = await db.Batches.Where(b => b.ProductId == product.Id && ids.Contains(b.Id)).ToListAsync();
        if (batches.Count != ids.Count) return ApiProblems.Validation("Lines", "بچ پیدا نشد یا مال این قلم نیست.");

        // «منقضی» نسبت به روز برداشت فیزیکی سنجیده می‌شود، نه روز ثبت؛ جعبه‌ای که دیروز، پیش از انقضا، برداشته شده امروز هم قابل ثبت است.
        var physicalDay = LocalDay.Today(time.OccurredAtUtc);
        var withdrawal = new Withdrawal
        {
            Id = Guid.NewGuid(),
            ProductId = product.Id,
            UserId = userId,
            AtUtc = now,
            OccurredAtUtc = time.OccurredAtUtc,
            IsLate = time.IsLate,
            // بخش ۷.۶: جعبه پیش از شمارش برداشته شده و بعد از آن ثبت می‌شود؛ احتمالاً انبارگردانی همین کسری را اصلاح کرده است.
            AfterStocktake = await db.StocktakeCounts.AnyAsync(c => ids.Contains(c.BatchId) && c.AtUtc > time.OccurredAtUtc),
            Status = WithdrawalStatus.Pending
        };
        db.Withdrawals.Add(withdrawal);

        foreach (var line in request.Lines)
        {
            var batch = batches.First(b => b.Id == line.BatchId);
            if (ExpiryDate.IsExpired(batch.ExpiryDate, physicalDay) && !product.AllowExpiredWithdrawal)
                return ApiProblems.Conflict($"بچ {batch.BatchNumber} منقضی است و برداشت از بچ منقضی برای این قلم مجاز نیست؛ برای خروج آن اسقاط ثبت شود.");
            if (batch.OnHand < line.Quantity)
                return ApiProblems.Conflict($"موجودی بچ {batch.BatchNumber} کمتر از تعداد خواسته‌شده است"
                    + (time.IsLate ? "؛ چون ثبت دیرهنگام است، به انباردار اطلاع دهید." : "؛ موجودی تغییر کرده است."));

            db.ExpectVersion(batch, line.BatchVersion);
            ledger.Post(batch, StockMovementKind.Withdrawal, -line.Quantity, time.OccurredAtUtc, note, withdrawal.Id);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        var item = (await ToItemsAsync(db, [withdrawal])).Single();
        return Results.Ok(item);
    }

    private static async Task<IResult> ListMineAsync(WarehouseDbContext db, AuditContext audit, int take = 50)
    {
        if (audit.UserId is not { } userId) return ApiProblems.Forbidden("کاربر شناخته نشد.");

        take = Math.Clamp(take, 1, MaxListTake);
        var page = await db.Withdrawals.AsNoTracking().Where(w => w.UserId == userId)
            .OrderByDescending(w => w.AtUtc).ThenByDescending(w => w.Id).Take(take).ToListAsync();
        return Results.Ok(await ToItemsAsync(db, page));
    }

    // نگاشت مشترک برداشت‌ها به WithdrawalItem؛ بررسی برداشت (برش ۳) هم از همین استفاده می‌کند.
    internal static async Task<List<WithdrawalItem>> ToItemsAsync(WarehouseDbContext db, IReadOnlyList<Withdrawal> withdrawals)
    {
        if (withdrawals.Count == 0) return [];

        var ids = withdrawals.Select(w => w.Id).ToList();
        var productIds = withdrawals.Select(w => w.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.WithdrawalId != null && ids.Contains(m.WithdrawalId.Value) && m.Kind == StockMovementKind.Withdrawal).ToListAsync();
        var batchIds = movements.Select(m => m.BatchId).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id);

        var userIds = withdrawals.Select(w => w.UserId).Concat(withdrawals.Where(w => w.ReviewedById != null).Select(w => w.ReviewedById!.Value)).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.UserName ?? "");
        string NameOf(Guid id) => names.TryGetValue(id, out var n) ? n : "—";

        var byWithdrawal = movements.ToLookup(m => m.WithdrawalId!.Value);
        return withdrawals.Select(w =>
        {
            var ms = byWithdrawal[w.Id].OrderBy(m => batches[m.BatchId].ExpiryDate is null).ThenBy(m => batches[m.BatchId].ExpiryDate).ThenBy(m => m.Id).ToList();
            var physicalDay = LocalDay.Today(w.OccurredAtUtc);
            var lines = ms.Select(m =>
            {
                var b = batches[m.BatchId];
                return new WithdrawalLineItem(b.Id, b.BatchNumber,
                    b.ExpiryDate is { } d ? ExpiryDate.Display(new ExpiryValue(d, b.ExpiryPrecision, b.ExpiryCalendar)) : null,
                    ExpiryDate.IsExpired(b.ExpiryDate, physicalDay), -m.Quantity);
            }).ToList();
            var p = products[w.ProductId];
            return new WithdrawalItem(w.Id, w.ProductId, p.Name, p.Brand, p.PackDescription, lines.Sum(l => l.Quantity), w.AtUtc, w.OccurredAtUtc,
                w.IsLate, w.AfterStocktake, lines.Any(l => l.IsExpired), w.Status, NameOf(w.UserId), ms.FirstOrDefault()?.Note, w.ReviewNote,
                w.ReviewedById is { } r ? NameOf(r) : null, w.ReviewedAtUtc, w.SelfReviewed, lines, w.Version);
        }).ToList();
    }
}
