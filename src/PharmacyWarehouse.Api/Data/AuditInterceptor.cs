using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PharmacyWarehouse.Api.Data;

// پیش از هر ذخیره، تغییرات موجودیت‌های IAudited را می‌خواند و ردیف تاریخچه می‌سازد و Version را زیاد می‌کند.
// به کد endpointها بستگی ندارد، پس فراموش نمی‌شود. ExecuteUpdate و SQL مستقیم از اینجا رد نمی‌شوند؛ پس ممنوع‌اند.
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    // مشتق‌اند یا کلید؛ تغییرشان خبر تازه‌ای نیست. هر ستونی که نامش با Key تمام شود هم ignore است (IsDerived).
    private static readonly HashSet<string> Ignored =
    [
        "Id", "Version", "SearchText", "CreatedAtUtc", "UpdatedAtUtc"
    ];

    private static bool IsDerived(string name) => Ignored.Contains(name) || name.EndsWith("Key", StringComparison.Ordinal);

    private readonly AuditContext _audit;

    public AuditInterceptor(AuditContext audit)
    {
        _audit = audit;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is WarehouseDbContext db)
        {
            await WriteAuditAsync(db, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is WarehouseDbContext db)
        {
            WriteAuditAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        }

        return base.SavingChanges(eventData, result);
    }

    private async Task WriteAuditAsync(WarehouseDbContext db, CancellationToken ct)
    {
        db.ChangeTracker.DetectChanges();

        var entries = db.ChangeTracker.Entries<IAudited>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        if (entries.Count == 0) return;

        var changeSetId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var rows = new List<AuditEntry>();

        foreach (var entry in entries)
        {
            AuditEntry Row(string action, string? field, string? oldValue, string? newValue) => new()
            {
                Id = Guid.NewGuid(),
                ChangeSetId = changeSetId,
                AtUtc = now,
                UserId = _audit.UserId,
                UserName = _audit.UserName,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entry.Entity.Id,
                SubjectId = AuditSubject.Of(entry.Entity),
                Action = action,
                Field = field,
                OldValue = oldValue,
                NewValue = newValue,
                Reason = _audit.Reason
            };

            switch (entry.State)
            {
                case EntityState.Added:
                    rows.Add(Row(AuditActions.Created, null, null, ToJson(entry, useOriginal: false)));
                    break;

                case EntityState.Deleted:
                    rows.Add(Row(AuditActions.Deleted, null, ToJson(entry, useOriginal: true), null));
                    break;

                case EntityState.Modified:
                    foreach (var property in entry.Properties)
                    {
                        var name = property.Metadata.Name;
                        if (IsDerived(name) || Equals(property.OriginalValue, property.CurrentValue)) continue;

                        rows.Add(Row(
                            AuditActions.Updated,
                            name,
                            await DescribeAsync(db, entry.Entity, name, property.OriginalValue, ct),
                            await DescribeAsync(db, entry.Entity, name, property.CurrentValue, ct)));
                    }

                    // endpoint مقدار OriginalValue را از Version دیده‌شده‌ی Client پر کرده است.
                    var version = entry.Property(e => e.Version);
                    version.CurrentValue = version.OriginalValue + 1;
                    break;
            }
        }

        db.AuditEntries.AddRange(rows);
    }

    private static string ToJson(EntityEntry<IAudited> entry, bool useOriginal)
    {
        var values = new Dictionary<string, string?>();
        foreach (var property in entry.Properties)
        {
            if (IsDerived(property.Metadata.Name)) continue;
            values[property.Metadata.Name] = Format(useOriginal ? property.OriginalValue : property.CurrentValue);
        }

        return JsonSerializer.Serialize(values);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    // شناسه‌ها به شکل id|name: تغییر نام بعدی یک واحد، تاریخچه را گمراه نمی‌کند.
    // ParentId در چند موجودیت هست؛ معنی‌اش به نوع موجودیت بستگی دارد.
    private static async Task<string?> DescribeAsync(WarehouseDbContext db, IAudited entity, string field, object? value, CancellationToken ct)
    {
        if (value is not Guid id) return Format(value);

        var name = (entity, field) switch
        {
            (StorageLocation, "ParentId") =>
                db.ChangeTracker.Entries<StorageLocation>().FirstOrDefault(e => e.Entity.Id == id)?.Entity.Name
                ?? await db.StorageLocations.AsNoTracking().Where(l => l.Id == id).Select(l => l.Name).FirstOrDefaultAsync(ct),
            (Product or Batch, "LocationId") =>
                db.ChangeTracker.Entries<StorageLocation>().FirstOrDefault(e => e.Entity.Id == id)?.Entity.Name
                ?? await db.StorageLocations.AsNoTracking().Where(l => l.Id == id).Select(l => l.Name).FirstOrDefaultAsync(ct),
            (Batch or ProductBarcode, "ProductId") =>
                db.ChangeTracker.Entries<Product>().FirstOrDefault(e => e.Entity.Id == id)?.Entity.Name
                ?? await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct),
            _ => null
        };

        return name is null ? id.ToString() : $"{id}|{name}";
    }
}
