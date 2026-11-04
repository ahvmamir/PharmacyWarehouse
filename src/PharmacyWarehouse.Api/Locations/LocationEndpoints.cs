using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Locations;

// ساختمان، طبقه و واحد. خواندن برای همه‌ی کارکنان، تغییر فقط مدیر. حذف واقعی ندارند.
public static class LocationEndpoints
{
    public static void MapLocationEndpoints(this WebApplication app)
    {
        var buildings = app.MapGroup(ApiRoutes.Buildings).RequireAuthorization(Permissions.LocationsManage);
        app.MapGet(ApiRoutes.Buildings, ListBuildingsAsync).RequireAuthorization(Permissions.EquipmentView);
        buildings.MapGet("{id:guid}/dependencies", BuildingDependenciesAsync);
        buildings.MapDelete("{id:guid}", DeleteBuildingAsync);
        buildings.MapPost("", CreateBuildingAsync);
        buildings.MapPut("{id:guid}", UpdateBuildingAsync);
        buildings.MapPost("{id:guid}/deactivate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetBuildingActiveAsync(id, r, false, db));
        buildings.MapPost("{id:guid}/activate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetBuildingActiveAsync(id, r, true, db));

        var floors = app.MapGroup(ApiRoutes.Floors).RequireAuthorization(Permissions.LocationsManage);
        app.MapGet(ApiRoutes.Floors, ListFloorsAsync).RequireAuthorization(Permissions.EquipmentView);
        floors.MapGet("{id:guid}/dependencies", FloorDependenciesAsync);
        floors.MapDelete("{id:guid}", DeleteFloorAsync);
        floors.MapPost("", CreateFloorAsync);
        floors.MapPut("{id:guid}", UpdateFloorAsync);
        floors.MapPost("{id:guid}/deactivate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetFloorActiveAsync(id, r, false, db));
        floors.MapPost("{id:guid}/activate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetFloorActiveAsync(id, r, true, db));

        var units = app.MapGroup(ApiRoutes.Units).RequireAuthorization(Permissions.LocationsManage);
        app.MapGet(ApiRoutes.Units, ListUnitsAsync).RequireAuthorization(Permissions.EquipmentView);
        units.MapGet("{id:guid}/dependencies", UnitDependenciesAsync);
        units.MapDelete("{id:guid}", DeleteUnitAsync);
        units.MapPost("", CreateUnitAsync);
        units.MapPut("{id:guid}", UpdateUnitAsync);
        units.MapPost("{id:guid}/deactivate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetUnitActiveAsync(id, r, false, db));
        units.MapPost("{id:guid}/activate", (Guid id, VersionRequest r, WarehouseDbContext db) => SetUnitActiveAsync(id, r, true, db));
    }

    private static BuildingItem ToItem(Building b) => new(b.Id, b.Name, b.IsActive, b.Version);
    private static FloorItem ToItem(Floor f) => new(f.Id, f.BuildingId, f.Name, f.SortOrder, f.IsActive, f.Version);
    private static UnitItem ToItem(Unit u) => new(u.Id, u.Name, u.IsActive, u.Version);

    // ذخیره با ترجمه‌ی خطاهای هم‌زمانی و یکتایی به ۴۰۹. مقایسه‌ی نام فقط روی NameKey است.
    private static async Task<IResult> SaveAsync(WarehouseDbContext db, Func<IResult> ok, string duplicateMessage)
    {
        try
        {
            await db.SaveChangesAsync();
            return ok();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return ApiProblems.Conflict(duplicateMessage);
        }
    }

    private static IResult? CheckName(string? name, int max, string label)
    {
        if (name is null) return ApiProblems.Validation("Name", $"نام {label} را وارد کنید.");
        return name.Length > max ? ApiProblems.Validation("Name", $"نام {label} حداکثر {max} نویسه است.") : null;
    }

    // حذف: فقط اگر چیزی به این رکورد تکیه نکرده باشد. پنجره‌ی Client همین فهرست را نشان می‌دهد.

    private const int MaxDependencies = 50;

    private static async Task<DependencyList> DevicesAsync(
        WarehouseDbContext db, IQueryable<Equipment> query, string kind, List<DependencyItem>? first = null)
    {
        var total = await query.CountAsync();
        var devices = await query
            .OrderBy(e => e.AssetNumberKey)
            .Take(MaxDependencies)
            .Select(e => new { e.Id, e.Type, e.AssetNumber })
            .ToListAsync();

        var items = first ?? [];
        items.AddRange(devices.Select(e => new DependencyItem(kind, $"{AuditLabels.Type(e.Type)} {e.AssetNumber}", e.Id)));

        return new DependencyList { Items = items, Total = total + (first?.Count ?? 0) };
    }

    private static async Task<DependencyList> FindBuildingDependenciesAsync(WarehouseDbContext db, Guid id)
    {
        var floors = await db.Floors.AsNoTracking()
            .Where(f => f.BuildingId == id)
            .OrderBy(f => f.SortOrder)
            .Select(f => new DependencyItem("طبقه", f.Name, null))
            .ToListAsync();

        return await DevicesAsync(db, db.Equipment.AsNoTracking().Where(e => e.BuildingId == id), "دستگاه", floors);
    }

    private static Task<DependencyList> FindFloorDependenciesAsync(WarehouseDbContext db, Guid id) =>
        DevicesAsync(db, db.Equipment.AsNoTracking().Where(e => e.FloorId == id), "دستگاه");

    private static Task<DependencyList> FindUnitDependenciesAsync(WarehouseDbContext db, Guid id) =>
        DevicesAsync(db, db.Equipment.AsNoTracking().Where(e => e.OwnerUnitId == id || e.LocationUnitId == id), "دستگاه");

    private static async Task<IResult> BuildingDependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.Buildings.AnyAsync(b => b.Id == id)
            ? Results.Ok(await FindBuildingDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> FloorDependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.Floors.AnyAsync(f => f.Id == id)
            ? Results.Ok(await FindFloorDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> UnitDependenciesAsync(Guid id, WarehouseDbContext db) =>
        await db.Units.AnyAsync(u => u.Id == id)
            ? Results.Ok(await FindUnitDependenciesAsync(db, id))
            : ApiProblems.NotFound();

    private static async Task<IResult> RemoveAsync(WarehouseDbContext db, object entity, DependencyList dependencies)
    {
        if (!dependencies.CanDelete) return ApiProblems.Conflict("این مورد وابستگی دارد و حذف نمی‌شود.");

        db.Remove(entity);
        return await SaveAsync(db, () => Results.Ok(new DependencyList()), "");
    }

    private static async Task<IResult> DeleteBuildingAsync(Guid id, WarehouseDbContext db)
    {
        var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == id);
        return building is null ? ApiProblems.NotFound() : await RemoveAsync(db, building, await FindBuildingDependenciesAsync(db, id));
    }

    private static async Task<IResult> DeleteFloorAsync(Guid id, WarehouseDbContext db)
    {
        var floor = await db.Floors.FirstOrDefaultAsync(f => f.Id == id);
        return floor is null ? ApiProblems.NotFound() : await RemoveAsync(db, floor, await FindFloorDependenciesAsync(db, id));
    }

    private static async Task<IResult> DeleteUnitAsync(Guid id, WarehouseDbContext db)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id);
        return unit is null ? ApiProblems.NotFound() : await RemoveAsync(db, unit, await FindUnitDependenciesAsync(db, id));
    }

    // ساختمان

    private static async Task<IResult> ListBuildingsAsync(WarehouseDbContext db)
    {
        var items = await db.Buildings.AsNoTracking().OrderBy(b => b.Name).ToListAsync();
        return Results.Ok(items.Select(ToItem));
    }

    private static async Task<IResult> CreateBuildingAsync(SaveBuildingRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxBuildingName, "ساختمان") is { } invalid) return invalid;

        const string duplicate = "ساختمانی با این نام وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Buildings.AnyAsync(b => b.NameKey == key)) return ApiProblems.Conflict(duplicate);

        var building = new Building { Id = Guid.NewGuid(), Name = name!, NameKey = key };
        db.Buildings.Add(building);
        return await SaveAsync(db, () => Results.Ok(ToItem(building)), duplicate);
    }

    private static async Task<IResult> UpdateBuildingAsync(Guid id, SaveBuildingRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxBuildingName, "ساختمان") is { } invalid) return invalid;

        var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == id);
        if (building is null) return ApiProblems.NotFound();

        const string duplicate = "ساختمانی با این نام وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Buildings.AnyAsync(b => b.NameKey == key && b.Id != id)) return ApiProblems.Conflict(duplicate);

        building.Name = name!;
        building.NameKey = key;
        db.ExpectVersion(building, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(building)), duplicate);
    }

    private static async Task<IResult> SetBuildingActiveAsync(Guid id, VersionRequest request, bool active, WarehouseDbContext db)
    {
        var building = await db.Buildings.FirstOrDefaultAsync(b => b.Id == id);
        if (building is null) return ApiProblems.NotFound();

        building.IsActive = active;
        db.ExpectVersion(building, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(building)), "");
    }

    // طبقه

    private static async Task<IResult> ListFloorsAsync(Guid? buildingId, WarehouseDbContext db)
    {
        var query = db.Floors.AsNoTracking();
        if (buildingId is { } filter) query = query.Where(f => f.BuildingId == filter);

        var items = await query.OrderBy(f => f.BuildingId).ThenBy(f => f.SortOrder).ThenBy(f => f.Name).ToListAsync();
        return Results.Ok(items.Select(ToItem));
    }

    private static async Task<IResult> CreateFloorAsync(SaveFloorRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxFloorName, "طبقه") is { } invalid) return invalid;

        var building = await db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == request.BuildingId);
        if (building is null) return ApiProblems.Validation("BuildingId", "ساختمان را انتخاب کنید.");
        if (!building.IsActive) return ApiProblems.Validation("BuildingId", "این ساختمان غیرفعال است.");

        const string duplicate = "این طبقه در همین ساختمان وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Floors.AnyAsync(f => f.BuildingId == request.BuildingId && f.NameKey == key))
        {
            return ApiProblems.Conflict(duplicate);
        }

        var floor = new Floor
        {
            Id = Guid.NewGuid(),
            BuildingId = request.BuildingId,
            Name = name!,
            NameKey = key,
            SortOrder = request.SortOrder
        };
        db.Floors.Add(floor);
        return await SaveAsync(db, () => Results.Ok(ToItem(floor)), duplicate);
    }

    private static async Task<IResult> UpdateFloorAsync(Guid id, SaveFloorRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxFloorName, "طبقه") is { } invalid) return invalid;

        var floor = await db.Floors.FirstOrDefaultAsync(f => f.Id == id);
        if (floor is null) return ApiProblems.NotFound();

        // جابه‌جایی طبقه بین ساختمان‌ها معنی ندارد و محل دستگاه‌ها را خراب می‌کند؛ BuildingId نادیده گرفته می‌شود.
        const string duplicate = "این طبقه در همین ساختمان وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Floors.AnyAsync(f => f.BuildingId == floor.BuildingId && f.NameKey == key && f.Id != id))
        {
            return ApiProblems.Conflict(duplicate);
        }

        floor.Name = name!;
        floor.NameKey = key;
        floor.SortOrder = request.SortOrder;
        db.ExpectVersion(floor, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(floor)), duplicate);
    }

    private static async Task<IResult> SetFloorActiveAsync(Guid id, VersionRequest request, bool active, WarehouseDbContext db)
    {
        var floor = await db.Floors.FirstOrDefaultAsync(f => f.Id == id);
        if (floor is null) return ApiProblems.NotFound();

        floor.IsActive = active;
        db.ExpectVersion(floor, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(floor)), "");
    }

    // واحد

    private static async Task<IResult> ListUnitsAsync(WarehouseDbContext db)
    {
        var items = await db.Units.AsNoTracking().OrderBy(u => u.Name).ToListAsync();
        return Results.Ok(items.Select(ToItem));
    }

    private static async Task<IResult> CreateUnitAsync(SaveUnitRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxUnitName, "واحد") is { } invalid) return invalid;

        const string duplicate = "واحدی با این نام وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Units.AnyAsync(u => u.NameKey == key)) return ApiProblems.Conflict(duplicate);

        var unit = new Unit { Id = Guid.NewGuid(), Name = name!, NameKey = key };
        db.Units.Add(unit);
        return await SaveAsync(db, () => Results.Ok(ToItem(unit)), duplicate);
    }

    private static async Task<IResult> UpdateUnitAsync(Guid id, SaveUnitRequest request, WarehouseDbContext db)
    {
        var name = TextNormalizer.Clean(request.Name);
        if (CheckName(name, LocationRules.MaxUnitName, "واحد") is { } invalid) return invalid;

        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null) return ApiProblems.NotFound();

        const string duplicate = "واحدی با این نام وجود دارد.";
        var key = TextNormalizer.Key(name)!;
        if (await db.Units.AnyAsync(u => u.NameKey == key && u.Id != id)) return ApiProblems.Conflict(duplicate);

        unit.Name = name!;
        unit.NameKey = key;
        db.ExpectVersion(unit, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(unit)), duplicate);
    }

    private static async Task<IResult> SetUnitActiveAsync(Guid id, VersionRequest request, bool active, WarehouseDbContext db)
    {
        var unit = await db.Units.FirstOrDefaultAsync(u => u.Id == id);
        if (unit is null) return ApiProblems.NotFound();

        unit.IsActive = active;
        db.ExpectVersion(unit, request.Version);
        return await SaveAsync(db, () => Results.Ok(ToItem(unit)), "");
    }
}
