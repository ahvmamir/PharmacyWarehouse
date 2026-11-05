using System.Globalization;
using System.Text.Encodings.Web;
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
        "Id", "Version", "SearchText", "CreatedAtUtc", "UpdatedAtUtc",
        "OnHand" // تاریخچه‌ی موجودی همان دفتر حرکت‌هاست (StockMovement)؛ ردیف تکراری فقط شلوغی است.
    ];

    private static readonly JsonSerializerOptions ReadableJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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

        // نام رکوردهای این ذخیره یک بار خوانده می‌شود؛ ChangeTracker.Entries هر بار DetectChanges می‌زند و در ورود گروهی کند است.
        var names = new Dictionary<Guid, string?>();
        foreach (var tracked in db.ChangeTracker.Entries())
        {
            if (tracked.Entity is StorageLocation location) names[location.Id] = location.Name;
            else if (tracked.Entity is Product product) names[product.Id] = product.Name;
        }

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
                    rows.Add(Row(AuditActions.Created, null, null, await ToJsonAsync(db, entry, names, useOriginal: false, ct)));
                    break;

                case EntityState.Deleted:
                    rows.Add(Row(AuditActions.Deleted, null, await ToJsonAsync(db, entry, names, useOriginal: true, ct), null));
                    break;

                case EntityState.Modified:
                    foreach (var property in entry.Properties)
                    {
                        var name = property.Metadata.Name;
                        if (IsDerived(name) || Equals(property.OriginalValue, property.CurrentValue)) continue;

                        rows.Add(Row(
                            AuditActions.Updated,
                            name,
                            await DescribeAsync(db, entry.Entity, name, property.OriginalValue, names, ct),
                            await DescribeAsync(db, entry.Entity, name, property.CurrentValue, names, ct)));
                    }

                    // endpoint مقدار OriginalValue را از Version دیده‌شده‌ی Client پر کرده است.
                    var version = entry.Property(e => e.Version);
                    version.CurrentValue = version.OriginalValue + 1;
                    break;
            }
        }

        db.AuditEntries.AddRange(rows);
    }

    private static async Task<string> ToJsonAsync(WarehouseDbContext db, EntityEntry<IAudited> entry, Dictionary<Guid, string?> names, bool useOriginal, CancellationToken ct)
    {
        var values = new Dictionary<string, string?>();
        foreach (var property in entry.Properties)
        {
            if (IsDerived(property.Metadata.Name)) continue;

            var value = useOriginal ? property.OriginalValue : property.CurrentValue;
            values[property.Metadata.Name] = await DescribeAsync(db, entry.Entity, property.Metadata.Name, value, names, ct);
        }

        // نویسه‌های فارسی escape نشوند تا ردیف‌ها خوانا ذخیره شوند. خروجی را Blazor موقع نمایش encode می‌کند.
        return JsonSerializer.Serialize(values, ReadableJson);
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), // قالب پیش‌فرض «ماه/روز/سال» مبهم است
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    // شناسه‌ها به شکل id|name: تغییر نام بعدی یک واحد، تاریخچه را گمراه نمی‌کند.
    // ParentId در چند موجودیت هست؛ معنی‌اش به نوع موجودیت بستگی دارد.
    private static async Task<string?> DescribeAsync(WarehouseDbContext db, IAudited entity, string field, object? value, Dictionary<Guid, string?> names, CancellationToken ct)
    {
        if (value is not Guid id) return Format(value);

        var kind = (entity, field) switch
        {
            (StorageLocation, "ParentId") or (Product or Batch, "LocationId") => typeof(StorageLocation),
            (Batch or ProductBarcode, "ProductId") => typeof(Product),
            _ => null
        };
        if (kind is null) return id.ToString();

        if (!names.TryGetValue(id, out var name))
        {
            name = kind == typeof(StorageLocation)
                ? await db.StorageLocations.AsNoTracking().Where(l => l.Id == id).Select(l => l.Name).FirstOrDefaultAsync(ct)
                : await db.Products.AsNoTracking().Where(p => p.Id == id).Select(p => p.Name).FirstOrDefaultAsync(ct);
            names[id] = name;
        }

        return name is null ? id.ToString() : $"{id}|{name}";
    }
}
