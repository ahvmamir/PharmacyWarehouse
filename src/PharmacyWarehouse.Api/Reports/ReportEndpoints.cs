using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.DataTools;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Reports;

public static class ReportEndpoints
{
    private const int MaxChangeRows = 5000;
    private const string CountColumn = "تعداد";

    public static void MapReportEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Reports + "/{key}", ViewAsync).RequireAuthorization(Permissions.ReportsView);
        app.MapGet(ApiRoutes.Reports + "/{key}/excel", ExcelAsync).RequireAuthorization(Permissions.DataExport);
        app.MapGet(ApiRoutes.RecentChangeSets, ChangeSetsAsync).RequireAuthorization(Permissions.ReportsView);
    }

    private static async Task<IResult> ViewAsync(
        string key, WarehouseDbContext db, Guid? ownerUnitId, Guid? buildingId, EquipmentType? type, EquipmentStatus? status, int? days)
    {
        var table = await BuildAsync(key, db, ownerUnitId, buildingId, type, status, days);
        return table is null ? ApiProblems.NotFound() : Results.Ok(table);
    }

    private static async Task<IResult> ExcelAsync(
        string key, WarehouseDbContext db, Guid? ownerUnitId, Guid? buildingId, EquipmentType? type, EquipmentStatus? status, int? days)
    {
        var table = await BuildAsync(key, db, ownerUnitId, buildingId, type, status, days);
        if (table is null) return ApiProblems.NotFound();

        var numeric = table.Columns
            .Select((name, index) => (name, index))
            .Where(c => c.name == CountColumn)
            .Select(c => c.index)
            .ToHashSet();

        var bytes = XlsxWriter.Write(new XlsxSheet(table.Title, table.Columns, table.Rows, numeric));
        return Results.File(bytes, DataEndpoints.XlsxContentType, $"emu-{key}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.xlsx");
    }

    private static async Task<ReportTable?> BuildAsync(
        string key, WarehouseDbContext db, Guid? ownerUnitId, Guid? buildingId, EquipmentType? type, EquipmentStatus? status, int? days)
    {
        var info = ReportCatalog.All.FirstOrDefault(r => r.Key == key);
        if (info is null) return null;

        var (columns, rows) = key switch
        {
            ReportCatalog.Equipment => await EquipmentAsync(db, ownerUnitId, buildingId, type, status),
            ReportCatalog.ByOwner => await ByOwnerAsync(db),
            ReportCatalog.ByLocation => await ByLocationAsync(db),
            ReportCatalog.ByTypeStatus => await ByTypeStatusAsync(db),
            ReportCatalog.TemporaryAssets => (EquipmentSheet.Columns, await EquipmentSheet.RowsAsync(
                db.Equipment.Where(e => e.HasTemporaryAssetNumber && e.Status != EquipmentStatus.Retired))),
            ReportCatalog.Components => await ComponentsAsync(db),
            ReportCatalog.NotVerified => await NotVerifiedAsync(db, days),
            ReportCatalog.Repairs => await RepairsAsync(db, days),
            _ => await RecentChangesAsync(db, days)
        };

        return new ReportTable { Title = info.Title, Columns = columns, Rows = rows, GeneratedAtUtc = DateTime.UtcNow };
    }

    private static async Task<(string[], List<string?[]>)> EquipmentAsync(
        WarehouseDbContext db, Guid? ownerUnitId, Guid? buildingId, EquipmentType? type, EquipmentStatus? status)
    {
        var query = db.Equipment.AsQueryable();

        if (ownerUnitId is { } owner) query = query.Where(e => e.OwnerUnitId == owner);
        if (buildingId is { } building) query = query.Where(e => e.BuildingId == building);
        if (type is { } t) query = query.Where(e => e.Type == t);
        query = status is { } s ? query.Where(e => e.Status == s) : query.Where(e => e.Status != EquipmentStatus.Retired);

        return (EquipmentSheet.Columns, await EquipmentSheet.RowsAsync(query));
    }

    private static async Task<(string[], List<string?[]>)> ByOwnerAsync(WarehouseDbContext db)
    {
        var groups = await db.Equipment.AsNoTracking()
            .Where(e => e.Status != EquipmentStatus.Retired)
            .GroupBy(e => new { Owner = e.OwnerUnit!.Name, e.Type })
            .Select(g => new { g.Key.Owner, g.Key.Type, Count = g.Count() })
            .ToListAsync();

        return (["واحد صاحب اموال", "نوع", CountColumn], groups
            .OrderBy(g => g.Owner).ThenBy(g => g.Type)
            .Select(g => new string?[] { g.Owner, AuditLabels.Type(g.Type), g.Count.ToString() })
            .ToList());
    }

    private static async Task<(string[], List<string?[]>)> ByLocationAsync(WarehouseDbContext db)
    {
        var groups = await db.Equipment.AsNoTracking()
            .Where(e => e.Status != EquipmentStatus.Retired)
            .GroupBy(e => new { Building = e.Building!.Name, Floor = e.Floor!.Name, e.Floor.SortOrder, Unit = e.LocationUnit!.Name })
            .Select(g => new { g.Key.Building, g.Key.Floor, g.Key.SortOrder, g.Key.Unit, Count = g.Count() })
            .ToListAsync();

        return (["ساختمان", "طبقه", "واحد محل", CountColumn], groups
            .OrderBy(g => g.Building).ThenBy(g => g.SortOrder).ThenBy(g => g.Unit)
            .Select(g => new string?[] { g.Building, g.Floor, g.Unit, g.Count.ToString() })
            .ToList());
    }

    private static async Task<(string[], List<string?[]>)> ByTypeStatusAsync(WarehouseDbContext db)
    {
        var groups = await db.Equipment.AsNoTracking()
            .GroupBy(e => new { e.Type, e.Status })
            .Select(g => new { g.Key.Type, g.Key.Status, Count = g.Count() })
            .ToListAsync();

        return (["نوع", "وضعیت", CountColumn], groups
            .OrderBy(g => g.Type).ThenBy(g => g.Status)
            .Select(g => new string?[] { AuditLabels.Type(g.Type), AuditLabels.Status(g.Status), g.Count.ToString() })
            .ToList());
    }

    private static async Task<(string[], List<string?[]>)> ComponentsAsync(WarehouseDbContext db)
    {
        var parts = await db.Components.AsNoTracking()
            .Join(db.Equipment.AsNoTracking().Where(e => e.Status != EquipmentStatus.Retired),
                c => c.EquipmentId, e => e.Id,
                (c, e) => new
                {
                    e.AssetNumber, e.AssetNumberKey, e.ComputerName, Owner = e.OwnerUnit!.Name,
                    c.Kind, c.Brand, c.Model, c.Capacity, c.Position
                })
            .OrderBy(c => c.AssetNumberKey).ThenBy(c => c.Position)
            .ToListAsync();

        return (["شماره اموال", "نام کامپیوتر", "صاحب اموال", "قطعه", "برند", "مدل", "ظرفیت"], parts
            .Select(c => new[] { c.AssetNumber, c.ComputerName, c.Owner, AuditLabels.Kind(c.Kind), c.Brand, c.Model, c.Capacity })
            .ToList());
    }

    // دستگاه غیراسقاطی که در این بازه حضورش تأیید نشده؛ هرگز‌تأییدنشده‌ها اول.
    private static async Task<(string[], List<string?[]>)> NotVerifiedAsync(WarehouseDbContext db, int? days)
    {
        var query = db.Equipment.AsNoTracking().Where(e => e.Status != EquipmentStatus.Retired);
        if (days is > 0)
        {
            var since = DateTime.UtcNow.AddDays(-days.Value);
            query = query.Where(e => e.LastVerifiedAtUtc == null || e.LastVerifiedAtUtc < since);
        }

        var rows = await query
            .OrderBy(e => e.LastVerifiedAtUtc).ThenBy(e => e.AssetNumberKey)
            .Select(e => new
            {
                e.AssetNumber, e.Type, e.Brand, e.Model, Owner = e.OwnerUnit!.Name, Building = e.Building!.Name,
                Floor = e.Floor!.Name, Unit = e.LocationUnit!.Name, e.Room, e.LastVerifiedAtUtc
            })
            .ToListAsync();

        return (["شماره اموال", "نوع", "برند", "مدل", "صاحب اموال", "ساختمان", "طبقه", "واحد محل", "اتاق", "آخرین تأیید حضور"], rows
            .Select(r => new[]
            {
                r.AssetNumber, AuditLabels.Type(r.Type), r.Brand, r.Model, r.Owner, r.Building, r.Floor, r.Unit, r.Room,
                r.LastVerifiedAtUtc is { } at ? JalaliDate.Format(at) : "هرگز"
            })
            .ToList());
    }

    private static async Task<(string[], List<string?[]>)> RepairsAsync(WarehouseDbContext db, int? days)
    {
        var query = db.Repairs.AsNoTracking();
        if (days is > 0)
        {
            var since = DateTime.UtcNow.AddDays(-days.Value);
            query = query.Where(r => r.SentAtUtc >= since || r.ReturnedAtUtc == null);
        }

        var rows = await query
            .Join(db.Equipment.AsNoTracking(), r => r.EquipmentId, e => e.Id, (r, e) => new
            {
                e.AssetNumber, e.Type, e.Brand, e.Model, Owner = e.OwnerUnit!.Name,
                r.SentAtUtc, r.Vendor, r.Problem, r.ReturnedAtUtc, r.Result, r.CostRials
            })
            .OrderByDescending(r => r.SentAtUtc)
            .ToListAsync();

        static string Days(DateTime from, DateTime? to) =>
            JalaliDate.ToPersianDigits(((int)((to ?? DateTime.UtcNow) - from).TotalDays).ToString());

        return (["شماره اموال", "نوع", "برند و مدل", "صاحب اموال", "تاریخ ارسال", "تعمیرگاه", "مشکل", "تاریخ برگشت", "نتیجه", "هزینه (ریال)", "روز"], rows
            .Select(r => new[]
            {
                r.AssetNumber, AuditLabels.Type(r.Type), $"{r.Brand} {r.Model}".Trim(), r.Owner,
                JalaliDate.FormatDate(r.SentAtUtc), r.Vendor, r.Problem,
                r.ReturnedAtUtc is { } back ? JalaliDate.FormatDate(back) : "هنوز برنگشته", r.Result,
                r.CostRials?.ToString("N0"), Days(r.SentAtUtc, r.ReturnedAtUtc)
            })
            .ToList());
    }

    private sealed record DeviceInfo(string? AssetNumber, EquipmentType? Type);

    private sealed record ChangeData(List<(AuditEntry Entry, ChangeLine Line)> Items, Dictionary<Guid, DeviceInfo> Devices);

    // ردیف‌های تاریخچه با متن خوانا. هم گزارش ریز (Excel) و هم فهرست گروه‌بندی‌شده از همین می‌خوانند.
    private static async Task<ChangeData> LoadChangesAsync(WarehouseDbContext db, int? days)
    {
        var query = db.AuditEntries.AsNoTracking();

        if (days is > 0)
        {
            var since = DateTime.UtcNow.AddDays(-days.Value);
            query = query.Where(a => a.AtUtc >= since);
        }

        var entries = await query.OrderByDescending(a => a.AtUtc).Take(MaxChangeRows).ToListAsync();

        var devices = (await db.Equipment.AsNoTracking().Select(e => new { e.Id, e.AssetNumber, e.Type }).ToListAsync())
            .ToDictionary(e => e.Id, e => new DeviceInfo(e.AssetNumber, e.Type));
        var buildings = await db.Buildings.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name);
        var floors = await db.Floors.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name);
        var units = await db.Units.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);

        // رکورد حذف‌شده دیگر در جدولش نیست؛ نامش را از ردیف‌های ثبت و حذف خود تاریخچه می‌خوانیم،
        // چون ردیف‌های «تغییر» همان رکورد نامی ندارند و ممکن است خارج از بازه‌ی این گزارش باشند.
        var deletedIds = entries.Where(a => a.Action == AuditActions.Deleted).Select(a => a.EntityId).Distinct().ToList();
        var missingIds = entries
            .Where(a => a.EntityType is "Equipment" or "Building" or "Floor" or "Unit" && a.Action != AuditActions.Deleted)
            .Select(a => a.EntityId)
            .Where(id => !devices.ContainsKey(id) && !buildings.ContainsKey(id) && !floors.ContainsKey(id) && !units.ContainsKey(id))
            .Concat(deletedIds)
            .Concat(entries.Where(a => a.EquipmentId is { } owner && !devices.ContainsKey(owner)).Select(a => a.EquipmentId!.Value))
            .Distinct()
            .ToList();

        var snapshots = await db.AuditEntries.AsNoTracking()
            .Where(a => missingIds.Contains(a.EntityId) && (a.Action == AuditActions.Created || a.Action == AuditActions.Deleted))
            .Select(a => new { a.EntityId, a.EntityType, Json = a.OldValue ?? a.NewValue })
            .ToListAsync();

        var formerNames = new Dictionary<Guid, string?>();
        foreach (var snapshot in snapshots)
        {
            var values = ReadJson(snapshot.Json);
            var name = values?.GetValueOrDefault("AssetNumber") ?? values?.GetValueOrDefault("Name");
            if (name is not null) formerNames[snapshot.EntityId] = name;

            // دستگاه حذف‌شده: نوع و شماره از عکس لحظه‌ی ثبت یا حذف.
            if (snapshot.EntityType == "Equipment" && !devices.ContainsKey(snapshot.EntityId))
            {
                var type = Enum.TryParse<EquipmentType>(values?.GetValueOrDefault("Type"), out var parsed) ? parsed : (EquipmentType?)null;
                devices[snapshot.EntityId] = new DeviceInfo(name, type);
            }
        }

        string? NameOf(Guid id, IReadOnlyDictionary<Guid, string> current) =>
            current.TryGetValue(id, out var name) ? name : formerNames.GetValueOrDefault(id);

        string? Asset(Guid id) => devices.TryGetValue(id, out var device) ? device.AssetNumber : formerNames.GetValueOrDefault(id);

        string? Subject(AuditEntry a) => a.EntityType switch
        {
            "Equipment" => $"دستگاه {Asset(a.EntityId)}",
            "Component" => $"قطعه {(Enum.TryParse<ComponentKind>(a.EntityLabel, out var kind) ? AuditLabels.Kind(kind) : a.EntityLabel)} در دستگاه {(a.EquipmentId is { } id ? Asset(id) : null)}",
            "Building" => $"ساختمان {NameOf(a.EntityId, buildings)}",
            "Floor" => $"طبقه {NameOf(a.EntityId, floors)}",
            "Unit" => $"واحد {NameOf(a.EntityId, units)}",
            "User" => $"کاربر {a.EntityLabel}",
            _ => a.EntityType
        };

        // مقدار JSON ثبت و حذف، جزئیات کل رکورد است و در یک ستون خوانا نیست.
        static string? Value(AuditEntry a, string? value) =>
            a.Action == AuditActions.Updated && value is not null ? AuditLabels.Value(a.Field, value) : null;

        var items = entries.Select(a => (a, new ChangeLine
        {
            Subject = Subject(a),
            Action = ActionLabel(a.Action),
            Field = a.Field is null ? null : AuditLabels.Field(a.Field),
            From = Value(a, a.OldValue),
            To = Value(a, a.NewValue)
        })).ToList();

        return new ChangeData(items, devices);
    }

    private static string ActionLabel(string action) => action switch
    {
        AuditActions.Created => "ثبت",
        AuditActions.Deleted => "حذف",
        _ => "تغییر"
    };

    private static async Task<(string[], List<string?[]>)> RecentChangesAsync(WarehouseDbContext db, int? days)
    {
        var data = await LoadChangesAsync(db, days);

        return (["زمان", "کاربر", "موضوع", "کار", "فیلد", "از", "به", "دلیل"], data.Items
            .Select(i => new[]
            {
                JalaliDate.Format(i.Entry.AtUtc), i.Entry.UserName, i.Line.Subject, i.Line.Action, i.Line.Field,
                i.Line.From, i.Line.To, i.Entry.Reason
            })
            .ToList());
    }

    // هر کار کاربر (یک ذخیره) یک ردیف: ثبت کیس با قطعاتش شش ردیف تاریخچه است ولی یک کار.
    private static async Task<IResult> ChangeSetsAsync(WarehouseDbContext db, int? days)
    {
        var data = await LoadChangesAsync(db, days);

        var sets = data.Items
            .GroupBy(i => i.Entry.ChangeSetId)
            .Select(group =>
            {
                var entries = group.Select(i => i.Entry).ToList();
                var first = entries[0];
                var (summary, equipmentId) = Summarize(entries, data.Devices, group.Last().Line.Subject);

                return new ChangeSetItem
                {
                    Id = group.Key,
                    AtUtc = entries.Max(e => e.AtUtc),
                    UserName = first.UserName,
                    Reason = entries.Select(e => e.Reason).FirstOrDefault(r => r is not null),
                    Summary = summary,
                    EquipmentId = equipmentId,
                    Lines = group.Select(i => i.Line).ToList()
                };
            })
            .OrderByDescending(s => s.AtUtc)
            .ToList();

        return Results.Ok(sets);
    }

    private static readonly HashSet<string> PlaceFields = ["BuildingId", "FloorId", "LocationUnitId", "Room", "Station", "StationPosition"];

    // یک جمله برای یک کار. فقط از ردیف‌های همین گروه و نام فعلی یا سابق دستگاه.
    private static (string Summary, Guid? EquipmentId) Summarize(List<AuditEntry> entries, IReadOnlyDictionary<Guid, DeviceInfo> devices, string? firstSubject)
    {
        static string N(int value) => JalaliDate.ToPersianDigits(value.ToString());

        var deviceIds = entries
            .Select(e => e.EntityType == "Equipment" ? e.EntityId : e.EquipmentId)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        string Label(Guid id)
        {
            var device = devices.GetValueOrDefault(id);
            var type = device?.Type is { } t ? AuditLabels.Type(t) : "دستگاه";
            return $"{type} {device?.AssetNumber}".Trim();
        }

        var deviceRows = entries.Where(e => e.EntityType == "Equipment").ToList();
        var fields = deviceRows.Where(e => e.Action == AuditActions.Updated && e.Field is not null).Select(e => e.Field!).ToHashSet();

        if (deviceIds.Count == 1)
        {
            var id = deviceIds[0];
            var label = Label(id);
            var exists = devices.ContainsKey(id) && entries.All(e => !(e.EntityType == "Equipment" && e.Action == AuditActions.Deleted));
            Guid? link = exists ? id : null;

            if (deviceRows.Any(e => e.Action == AuditActions.Created))
            {
                var parts = entries.Count(e => e.EntityType == "Component" && e.Action == AuditActions.Created);
                return ($"ثبت {label}{(parts > 0 ? $" با {N(parts)} قطعه" : "")}", link);
            }

            if (deviceRows.Any(e => e.Action == AuditActions.Deleted)) return ($"حذف {label}", null);

            if (fields.Contains("ParentId"))
            {
                var row = deviceRows.First(e => e.Field == "ParentId");
                return (row.NewValue is null ? $"جدا کردن {label}" : $"اتصال {label}", link);
            }

            if (fields.Count > 0 && fields.All(PlaceFields.Contains)) return ($"جابه‌جایی {label}", link);

            if (fields.Count == 1 && fields.Contains("Status"))
            {
                var row = deviceRows.First(e => e.Field == "Status");
                var to = Enum.TryParse<EquipmentStatus>(row.NewValue, out var status) ? AuditLabels.Status(status) : row.NewValue;
                return ($"تغییر وضعیت {label} به {to}", link);
            }

            if (fields.Count == 0) return ($"ویرایش قطعات {label}", link);

            return ($"ویرایش {label} ({N(entries.Count)} تغییر)", link);
        }

        if (deviceIds.Count > 1)
        {
            if (deviceRows.Count(e => e.Action == AuditActions.Created) == deviceIds.Count) return ($"ثبت {N(deviceIds.Count)} دستگاه", null);
            if (fields.Count > 0 && fields.All(PlaceFields.Contains)) return ($"جابه‌جایی {N(deviceIds.Count)} دستگاه", null);
            if (fields.Contains("ParentId")) return ($"تغییر اتصال {N(deviceIds.Count)} دستگاه", null);
            return ($"تغییر {N(deviceIds.Count)} دستگاه", null);
        }

        // ساختمان، طبقه، واحد، کاربر.
        var first = entries[^1];
        var subjects = entries.Select(e => e.EntityId).Distinct().Count();
        var more = subjects > 1 ? $" و {N(subjects - 1)} مورد دیگر" : "";
        return ($"{ActionLabel(first.Action)} {firstSubject}{more}", null);
    }

    private static Dictionary<string, string?>? ReadJson(string? value)
    {
        if (value is null || !value.StartsWith('{')) return null;

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string?>>(value);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
