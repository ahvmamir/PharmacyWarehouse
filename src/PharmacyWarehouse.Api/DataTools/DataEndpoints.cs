using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Assets;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.DataTools;

public static class DataEndpoints
{
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private const long MaxUploadBytes = 20 * 1024 * 1024;
    private const int MaxImportRows = 20000;

    public static void MapDataEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.DataTemplate, (string? types) =>
                ParseTypes(types, out var parsed) is { } error ? error : Template(parsed))
            .RequireAuthorization(Permissions.DataImport);
        app.MapGet(ApiRoutes.DataExport, ExportAsync).RequireAuthorization(Permissions.DataExport);
        app.MapGet(ApiRoutes.DataBackup, BackupAsync).RequireAuthorization(Permissions.BackupDownload);

        // فرم چندبخشی؛ حفاظت CSRF همان هدر مشترک است.
        app.MapPost(ApiRoutes.DataImportPreview, (IFormFile file, bool? createMissing, string? types, WarehouseDbContext db, AuditContext audit) =>
                ParseTypes(types, out var parsed) is { } error ? Task.FromResult(error) : ImportFileAsync(file, createMissing == true, parsed, commit: false, db, audit))
            .RequireAuthorization(Permissions.DataImport)
            .DisableAntiforgery();
        app.MapPost(ApiRoutes.DataImportCommit, (IFormFile file, bool? createMissing, string? types, WarehouseDbContext db, AuditContext audit) =>
                ParseTypes(types, out var parsed) is { } error ? Task.FromResult(error) : ImportFileAsync(file, createMissing == true, parsed, commit: true, db, audit))
            .RequireAuthorization(Permissions.DataImport)
            .DisableAntiforgery();

        // ردیف‌های اصلاح‌شده در مرورگر (JSON)؛ همان اعتبارسنجی ورود از فایل.
        app.MapPost(ApiRoutes.DataImportCheck, (ImportRowsRequest request, WarehouseDbContext db, AuditContext audit) =>
                ImportRowsAsync(request.Rows, request.CreateMissing, ValidTypes(request.Types), commit: false, db, audit))
            .RequireAuthorization(Permissions.DataImport);
        app.MapPost(ApiRoutes.DataImportCommitRows, (ImportRowsRequest request, WarehouseDbContext db, AuditContext audit) =>
                ImportRowsAsync(request.Rows, request.CreateMissing, ValidTypes(request.Types), commit: true, db, audit))
            .RequireAuthorization(Permissions.DataImport);
    }

    // «Case,Monitor» به فهرست نوع. خالی یعنی همه. نوع ناشناخته ۴۰۰ است، نه نادیده گرفتن بی‌صدا.
    private static IResult? ParseTypes(string? text, out EquipmentType[] types)
    {
        types = [];
        if (string.IsNullOrWhiteSpace(text)) return null;

        var list = new List<EquipmentType>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<EquipmentType>(part, ignoreCase: true, out var type) || !Enum.IsDefined(type))
            {
                return ApiProblems.Validation("types", $"نوع «{part}» نامعتبر است.");
            }
            list.Add(type);
        }

        types = list.Distinct().ToArray();
        return null;
    }

    private static EquipmentType[] ValidTypes(EquipmentType[]? types) =>
        (types ?? []).Where(Enum.IsDefined).Distinct().ToArray();

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    private static IResult Template(EquipmentType[] types)
    {
        var columns = EquipmentSheet.ColumnsFor(types);

        // همه‌ی نوع‌ها: یک نمونه‌ی مانیتور، مثل قبل. نوع‌های انتخاب‌شده: یک نمونه برای هر کدام.
        var samples = types.Length == 0 ? [EquipmentType.Monitor] : types;
        var rows = samples.Select((type, i) =>
        {
            var values = new Dictionary<string, string?>
            {
                [EquipmentSheet.Type] = AuditLabels.Type(type),
                [EquipmentSheet.AssetNumber] = $"TEST-{i + 1:0000}",
                [EquipmentSheet.Serial] = $"SN-{i + 1:0000}",
                [EquipmentSheet.Brand] = "TestBrand",
                [EquipmentSheet.Model] = "TestModel",
                [EquipmentSheet.Size] = type == EquipmentType.Monitor ? "22" : null,
                [EquipmentSheet.ComputerName] = EquipmentRules.IsHub(type) ? $"TEST-PC-{i + 1:00}" : null,
                [EquipmentSheet.PeripheralKind] = type == EquipmentType.Peripheral ? "اسپیکر" : null,
                [EquipmentSheet.Owner] = "واحد نمونه",
                [EquipmentSheet.Building] = "ساختمان نمونه",
                [EquipmentSheet.Floor] = "همکف",
                [EquipmentSheet.LocationUnit] = "واحد نمونه",
                [EquipmentSheet.Room] = "اتاق 1",
                [EquipmentSheet.Status] = AuditLabels.Status(EquipmentStatus.Active)
            };
            return columns.Select(c => values.GetValueOrDefault(c)).ToArray();
        }).ToList();

        var suffix = types.Length == 0 ? "" : "-" + string.Join('-', types.Select(t => t.ToString().ToLowerInvariant()));
        var bytes = XlsxWriter.Write(new XlsxSheet("تجهیزات", columns, rows));
        return Results.File(bytes, XlsxContentType, $"emu-import-template{suffix}.xlsx");
    }

    // بکاپ خوانا: همه‌ی جدول‌ها در یک فایل Excel.
    private static async Task<IResult> ExportAsync(WarehouseDbContext db)
    {
        var devices = await EquipmentSheet.RowsAsync(db.Equipment);

        var components = await db.Components.AsNoTracking()
            .Join(db.Equipment.AsNoTracking(), c => c.EquipmentId, e => e.Id, (c, e) => new { e.AssetNumber, e.AssetNumberKey, c.Kind, c.Brand, c.Model, c.Capacity, c.Position })
            .OrderBy(c => c.AssetNumberKey).ThenBy(c => c.Position)
            .ToListAsync();

        var buildings = await db.Buildings.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        var floors = await db.Floors.AsNoTracking()
            .Join(db.Buildings.AsNoTracking(), f => f.BuildingId, b => b.Id, (f, b) => new { Building = b.Name, f.Name, f.SortOrder, f.IsActive })
            .OrderBy(f => f.Building).ThenBy(f => f.SortOrder)
            .ToListAsync();
        var units = await db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync();

        static string Active(bool active) => active ? "فعال" : "غیرفعال";

        var bytes = XlsxWriter.Write(
            new XlsxSheet("تجهیزات", EquipmentSheet.Columns, devices),
            new XlsxSheet("قطعات", ["شماره اموال دستگاه", "قطعه", "برند", "مدل", "ظرفیت"],
                components.Select(c => new[] { c.AssetNumber, AuditLabels.Kind(c.Kind), c.Brand, c.Model, c.Capacity })),
            new XlsxSheet("ساختمان‌ها", ["نام", "وضعیت"], buildings.Select(b => new[] { b.Name, Active(b.IsActive) })),
            new XlsxSheet("طبقه‌ها", ["ساختمان", "طبقه", "ترتیب", "وضعیت"],
                floors.Select(f => new[] { f.Building, f.Name, f.SortOrder.ToString(), Active(f.IsActive) }), new HashSet<int> { 2 }),
            new XlsxSheet("واحدها", ["نام", "وضعیت"], units.Select(u => new[] { u.Name, Active(u.IsActive) })));

        return Results.File(bytes, XlsxContentType, $"emu-data-{Stamp()}.xlsx");
    }

    // بکاپ کامل: نسخه‌ی سازگار دیتابیس، با تاریخچه و کاربران. فقط با مجوز مخصوص، چون hash رمزها در آن است.
    private static async Task<IResult> BackupAsync(WarehouseDbContext db, Operations.DatabaseLocation database)
    {
        var target = Path.Combine(Path.GetTempPath(), $"emu-backup-{Guid.NewGuid():N}.db");
        try
        {
            await Operations.OperationCommands.VacuumIntoAsync(db, target);
            var bytes = await File.ReadAllBytesAsync(target);
            Operations.BackupMarker.Write(database.FilePath, "دانلود از برنامه");
            return Results.File(bytes, "application/octet-stream", $"emu-{Stamp()}.db");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(target)) File.Delete(target);
        }
    }

    private static TEnum? ParseLabel<TEnum>(string? text, Func<TEnum, string> label) where TEnum : struct, Enum
    {
        if (text is null) return null;

        var key = TextNormalizer.Key(text);
        foreach (var value in Enum.GetValues<TEnum>())
        {
            if (TextNormalizer.Key(label(value)) == key || value.ToString().Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return null;
    }

    // فایل به ردیف‌ها تبدیل می‌شود و همان مسیر ردیف‌های اصلاح‌شده را می‌رود.
    private static async Task<IResult> ImportFileAsync(IFormFile file, bool createMissing, EquipmentType[] types, bool commit, WarehouseDbContext db, AuditContext audit)
    {
        if (file.Length == 0) return ApiProblems.Validation("file", "فایل خالی است.");
        if (file.Length > MaxUploadBytes) return ApiProblems.Validation("file", "حجم فایل بیشتر از ۲۰ مگابایت است.");

        List<string?[]> sheet;
        try
        {
            await using var stream = file.OpenReadStream();
            sheet = XlsxReader.ReadFirstSheet(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return ApiProblems.Validation("file", "این فایل xlsx معتبر نیست.");
        }

        if (sheet.Count == 0) return ApiProblems.Validation("file", "فایل هیچ ردیفی ندارد.");
        if (sheet.Count > MaxImportRows) return ApiProblems.Validation("file", $"حداکثر {MaxImportRows} ردیف در هر فایل.");

        var header = new Dictionary<string, int>();
        for (var i = 0; i < sheet[0].Length; i++)
        {
            if (TextNormalizer.Key(sheet[0][i]) is { } key) header.TryAdd(key, i);
        }

        // با یک نوع، ستون «نوع» لازم نیست؛ همه‌ی ردیف‌ها همان نوع‌اند.
        var required = new List<string> { EquipmentSheet.Owner, EquipmentSheet.Building, EquipmentSheet.Floor, EquipmentSheet.LocationUnit };
        if (types.Length != 1) required.Insert(0, EquipmentSheet.Type);

        foreach (var column in required)
        {
            if (!header.ContainsKey(TextNormalizer.Key(column)!))
            {
                return ApiProblems.Validation("file", $"ستون «{column}» در ردیف اول فایل نیست. از فایل نمونه‌ی همین نوع استفاده کنید.");
            }
        }

        var rows = new List<ImportRow>();
        for (var index = 1; index < sheet.Count; index++)
        {
            var cells = sheet[index];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            var values = new Dictionary<string, string?>();
            foreach (var column in EquipmentSheet.Columns)
            {
                if (header.TryGetValue(TextNormalizer.Key(column)!, out var at) && at < cells.Length)
                {
                    values[column] = TextNormalizer.Clean(cells[at]);
                }
            }

            // شماره‌ی سطر مثل خود Excel.
            rows.Add(new ImportRow { Row = index + 1, Values = values });
        }

        return await ImportRowsAsync(rows, createMissing, types, commit, db, audit);
    }

    // پیش‌نمایش، بررسی دوباره و ثبت یک مسیر مشترک دارند؛ فقط ثبت چیزی ذخیره می‌کند. همه از رهگیر می‌گذرند.
    private static async Task<IResult> ImportRowsAsync(List<ImportRow>? rows, bool createMissing, EquipmentType[] types, bool commit, WarehouseDbContext db, AuditContext audit)
    {
        rows ??= [];
        if (rows.Count == 0) return ApiProblems.Validation("rows", "ردیفی برای ورود نیست.");
        if (rows.Count > MaxImportRows) return ApiProblems.Validation("rows", $"حداکثر {MaxImportRows} ردیف در هر ورود.");

        var units = await db.Units.ToDictionaryAsync(u => u.NameKey);
        var buildings = await db.Buildings.ToDictionaryAsync(b => b.NameKey);
        var floors = (await db.Floors.ToListAsync()).ToDictionary(f => (f.BuildingId, f.NameKey));
        var usedAssets = (await db.Equipment.AsNoTracking()
                .Where(e => e.AssetNumberKey != null)
                .Select(e => e.AssetNumberKey!)
                .ToListAsync())
            .ToHashSet();

        var created = new List<string>();
        var problems = new List<ImportRowResult>();
        var now = DateTime.UtcNow;
        var imported = 0;
        var total = 0;
        var temporaryOffset = 0;

        Unit? ResolveUnit(string? name, List<string> errors, string label)
        {
            var key = TextNormalizer.Key(name);
            if (key is null) { errors.Add($"{label} خالی است."); return null; }
            if (units.TryGetValue(key, out var unit))
            {
                if (!unit.IsActive) errors.Add($"واحد «{name}» غیرفعال است.");
                return unit;
            }

            if (!createMissing) { errors.Add($"واحد «{name}» تعریف نشده است."); return null; }

            unit = new Unit { Id = Guid.NewGuid(), Name = name!, NameKey = key };
            units[key] = unit;
            created.Add($"واحد: {name}");
            if (commit) db.Units.Add(unit);
            return unit;
        }

        Building? ResolveBuilding(string? name, List<string> errors)
        {
            var key = TextNormalizer.Key(name);
            if (key is null) { errors.Add("ساختمان خالی است."); return null; }
            if (buildings.TryGetValue(key, out var building))
            {
                if (!building.IsActive) errors.Add($"ساختمان «{name}» غیرفعال است.");
                return building;
            }

            if (!createMissing) { errors.Add($"ساختمان «{name}» تعریف نشده است."); return null; }

            building = new Building { Id = Guid.NewGuid(), Name = name!, NameKey = key };
            buildings[key] = building;
            created.Add($"ساختمان: {name}");
            if (commit) db.Buildings.Add(building);
            return building;
        }

        Floor? ResolveFloor(Building building, string? name, List<string> errors)
        {
            var key = TextNormalizer.Key(name);
            if (key is null) { errors.Add("طبقه خالی است."); return null; }
            if (floors.TryGetValue((building.Id, key), out var floor))
            {
                if (!floor.IsActive) errors.Add($"طبقه «{name}» غیرفعال است.");
                return floor;
            }

            if (!createMissing) { errors.Add($"طبقه «{name}» در ساختمان «{building.Name}» تعریف نشده است."); return null; }

            floor = new Floor { Id = Guid.NewGuid(), BuildingId = building.Id, Name = name!, NameKey = key };
            floors[(building.Id, key)] = floor;
            created.Add($"طبقه: {building.Name}، {name}");
            if (commit) db.Floors.Add(floor);
            return floor;
        }

        var checkedRows = new List<ImportRow>();

        foreach (var source in rows)
        {
            var values = source.Values ?? [];
            string? Cell(string column) => values.TryGetValue(column, out var value) ? TextNormalizer.Clean(value) : null;
            bool Flag(string column) => Cell(column)?.ToLowerInvariant() is "بله" or "دارد" or "yes" or "true" or "1";

            if (values.Values.All(string.IsNullOrWhiteSpace)) continue;

            total++;
            var errors = new List<string>();

            var typeText = Cell(EquipmentSheet.Type);
            EquipmentType? type;
            if (typeText is null && types.Length == 1)
            {
                type = types[0];
            }
            else if (typeText is null)
            {
                type = null;
                errors.Add(types.Length == 0
                    ? "نوع خالی است."
                    : $"نوع خالی است؛ یکی از: {string.Join("، ", types.Select(AuditLabels.Type))}.");
            }
            else
            {
                type = ParseLabel<EquipmentType>(typeText, AuditLabels.Type);
                if (type is null)
                {
                    errors.Add($"نوع «{typeText}» نامعتبر است.");
                }
                else if (types.Length > 0 && !types.Contains(type.Value))
                {
                    errors.Add($"نوع «{typeText}» در این ورود انتخاب نشده است ({string.Join("، ", types.Select(AuditLabels.Type))}).");
                    type = null;
                }
            }

            var statusText = Cell(EquipmentSheet.Status);
            var status = statusText is null ? EquipmentStatus.Active : ParseLabel<EquipmentStatus>(statusText, AuditLabels.Status);
            if (status is null) errors.Add($"وضعیت «{statusText}» نامعتبر است.");

            var owner = ResolveUnit(Cell(EquipmentSheet.Owner), errors, "واحد صاحب اموال");
            var location = ResolveUnit(Cell(EquipmentSheet.LocationUnit), errors, "واحد محل");
            var building = ResolveBuilding(Cell(EquipmentSheet.Building), errors);
            var floor = building is null ? null : ResolveFloor(building, Cell(EquipmentSheet.Floor), errors);

            var assetText = Cell(EquipmentSheet.AssetNumber);
            var mouseModel = Cell(EquipmentSheet.MouseModel);
            var keyboardModel = Cell(EquipmentSheet.KeyboardModel);

            var input = new EquipmentInput
            {
                Type = type ?? EquipmentType.Case,
                AssetNumber = assetText,
                UseTemporaryAssetNumber = assetText is null,
                SerialNumber = Cell(EquipmentSheet.Serial),
                Brand = Cell(EquipmentSheet.Brand),
                Model = Cell(EquipmentSheet.Model),
                SizeText = Cell(EquipmentSheet.Size),
                ComputerName = Cell(EquipmentSheet.ComputerName),
                PeripheralKind = Cell(EquipmentSheet.PeripheralKind),
                HasMouse = Flag(EquipmentSheet.Mouse) || mouseModel is not null,
                MouseModel = mouseModel,
                HasKeyboard = Flag(EquipmentSheet.Keyboard) || keyboardModel is not null,
                KeyboardModel = keyboardModel,
                OtherAccessories = Cell(EquipmentSheet.OtherAccessories),
                OwnerUnitId = owner?.Id ?? Guid.Empty,
                BuildingId = building?.Id ?? Guid.Empty,
                FloorId = floor?.Id ?? Guid.Empty,
                LocationUnitId = location?.Id ?? Guid.Empty,
                Room = Cell(EquipmentSheet.Room),
                Station = Cell(EquipmentSheet.Station),
                StationPosition = Cell(EquipmentSheet.StationPosition),
                Notes = Cell(EquipmentSheet.Notes)
            };

            // خطاهای محل نامعلوم را همان بالا گفته‌ایم؛ تکرار نمی‌کنیم.
            if (type is not null)
            {
                errors.AddRange(EquipmentRules.Validate(input)
                    .Where(e => e.Field is not (nameof(input.OwnerUnitId) or nameof(input.BuildingId) or nameof(input.FloorId) or nameof(input.LocationUnitId)))
                    .Select(e => e.Message));
            }

            var assetKey = TextNormalizer.Key(assetText);
            if (assetKey is not null && usedAssets.Contains(assetKey))
            {
                errors.Add($"شماره اموال «{assetText}» تکراری است.");
            }

            var summary = string.Join(' ', new[] { type is { } t ? AuditLabels.Type(t) : typeText, assetText, Cell(EquipmentSheet.Brand), Cell(EquipmentSheet.Model) }
                .Where(v => v is not null));

            checkedRows.Add(new ImportRow { Row = source.Row, Values = values, Errors = errors.ToArray() });
            if (errors.Count > 0)
            {
                problems.Add(new ImportRowResult { Row = source.Row, Summary = summary, Errors = errors.ToArray() });
                continue;
            }

            if (assetKey is not null) usedAssets.Add(assetKey);
            imported++;

            if (!commit) continue;

            var equipment = new Equipment
            {
                Id = Guid.NewGuid(),
                Type = input.Type,
                Status = status!.Value,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                BuildingId = input.BuildingId,
                FloorId = input.FloorId,
                LocationUnitId = input.LocationUnitId,
                Room = input.Room,
                Station = input.Station,
                StationPosition = input.StationPosition,
                HasTemporaryAssetNumber = input.UseTemporaryAssetNumber,
                AssetNumber = input.UseTemporaryAssetNumber
                    ? await EquipmentEndpoints.NextTemporaryAssetNumberAsync(db, temporaryOffset++)
                    : assetText,
                AssetNumberKey = assetKey
            };

            equipment.AssetNumberKey ??= TextNormalizer.Key(equipment.AssetNumber);
            EquipmentEndpoints.ApplySpecs(equipment, input);
            db.Equipment.Add(equipment);
        }

        var result = new ImportResult
        {
            Committed = commit,
            Total = total,
            Valid = imported,
            Imported = commit ? imported : 0,
            WillCreate = created.ToArray(),
            Problems = problems,
            Columns = EquipmentSheet.ColumnsFor(types),
            Rows = checkedRows
        };

        if (!commit) return Results.Ok(result);

        audit.Reason = "ورود از Excel";

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict("هم‌زمان با این ورود، داده‌ی تکراری ثبت شد. دوباره پیش‌نمایش بگیرید.");
        }

        return Results.Ok(result);
    }
}
