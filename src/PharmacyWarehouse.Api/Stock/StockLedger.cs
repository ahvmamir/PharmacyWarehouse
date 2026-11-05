using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Stock;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Stock;

// تنها کدی که حرکت اضافه می‌کند و OnHand بچ را عوض می‌کند. SaveChanges را فراخواننده می‌زند
// تا حرکت، OnHand و بقیه‌ی تغییرات یک تراکنش باشند. فراخواننده پیش از Post باید db.ExpectVersion(batch, ...) بزند؛
// رهگیر Version بچ را زیاد می‌کند، پس دو ذخیره‌ی هم‌زمان روی یک بچ یکی ۴۰۹ می‌گیرد.
public sealed class StockLedger
{
    private readonly WarehouseDbContext _db;
    private readonly AuditContext _audit;

    public StockLedger(WarehouseDbContext db, AuditContext audit)
    {
        _db = db;
        _audit = audit;
    }

    public StockMovement Post(Batch batch, StockMovementKind kind, int quantity, DateTime occurredAtUtc, string? note = null, Guid? withdrawalId = null, Guid? reversesId = null, Guid? stocktakeCountId = null)
    {
        if (quantity == 0) throw new InvalidOperationException("مقدار حرکت نمی‌تواند صفر باشد.");
        if (_db.Entry(batch).State == EntityState.Detached) throw new InvalidOperationException("بچ باید در DbContext ردیابی شود.");

        var next = batch.OnHand + quantity;
        if (next < 0) throw new InvalidOperationException("موجودی بچ نمی‌تواند منفی شود.");

        var movement = new StockMovement
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Kind = kind,
            Quantity = quantity,
            AtUtc = DateTime.UtcNow, // زمان ثبت همیشه اکنونِ سرور است، نه ورودی فراخواننده
            OccurredAtUtc = occurredAtUtc,
            UserId = _audit.UserId,
            WithdrawalId = withdrawalId,
            ReversesId = reversesId,
            StocktakeCountId = stocktakeCountId,
            Note = TextNormalizer.Clean(note)
        };
        _db.StockMovements.Add(movement);
        batch.OnHand = next;
        return movement;
    }
}
