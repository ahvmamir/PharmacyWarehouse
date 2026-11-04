using System.Linq.Expressions;

using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Assets;

public static class EquipmentEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static void MapEquipmentEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(ApiRoutes.Equipment);

        group.MapGet("", ListAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapGet("summary", SummaryAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapGet("lookup", LookupAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapGet("{id:guid}", GetAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapPost("", CreateAsync).RequireAuthorization(Permissions.EquipmentManage);
        group.MapPost("bulk", CreateBulkAsync).RequireAuthorization(Permissions.EquipmentManage);
        group.MapPut("{id:guid}", UpdateAsync).RequireAuthorization(Permissions.EquipmentManage);
        group.MapPost("{id:guid}/move", MoveAsync).RequireAuthorization(Permissions.EquipmentMove);
        group.MapPost("{id:guid}/verify", VerifyAsync).RequireAuthorization(Permissions.EquipmentVerify);
        group.MapGet("{id:guid}/repairs", RepairsAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapPost("{id:guid}/repairs", SendToRepairAsync).RequireAuthorization(Permissions.EquipmentChangeStatus);
        app.MapPost("/api/repairs/{repairId:guid}/return", ReturnFromRepairAsync).RequireAuthorization(Permissions.EquipmentChangeStatus);
        group.MapPost("bulk-move", BulkMoveAsync).RequireAuthorization(Permissions.EquipmentMove);
        group.MapPost("bulk-status", BulkStatusAsync).RequireAuthorization(Permissions.EquipmentChangeStatus);
        group.MapPost("{id:guid}/status", ChangeStatusAsync).RequireAuthorization(Permissions.EquipmentChangeStatus);
        group.MapPost("{id:guid}/link", LinkAsync).RequireAuthorization(Permissions.EquipmentLink);
        group.MapPost("{id:guid}/unlink", UnlinkAsync).RequireAuthorization(Permissions.EquipmentLink);
        group.MapGet("{id:guid}/history", HistoryAsync).RequireAuthorization(Permissions.EquipmentView);
        group.MapGet("{id:guid}/dependencies", DependenciesAsync).RequireAuthorization(Permissions.EquipmentDelete);
        group.MapDelete("{id:guid}", DeleteAsync).RequireAuthorization(Permissions.EquipmentDelete);
    }

    private static readonly Expression<Func<Equipment, EquipmentListItem>> ToListItem = e => new EquipmentListItem
    {
        Id = e.Id,
        Type = e.Type,
        Status = e.Status,
        AssetNumber = e.AssetNumber,
        HasTemporaryAssetNumber = e.HasTemporaryAssetNumber,
        SerialNumber = e.SerialNumber,
        Brand = e.Brand,
        Model = e.Model,
        ComputerName = e.ComputerName,
        PeripheralKind = e.PeripheralKind,
        ParentId = e.ParentId,
        OwnerUnitName = e.OwnerUnit!.Name,
        UpdatedAtUtc = e.UpdatedAtUtc,
        LastVerifiedAtUtc = e.LastVerifiedAtUtc,
        BuildingName = e.Building!.Name,
        FloorName = e.Floor!.Name,
        LocationUnitName = e.LocationUnit!.Name,
        Room = e.Room,
        BuildingId = e.BuildingId,
        FloorId = e.FloorId,
        LocationUnitId = e.LocationUnitId,
        Version = e.Version
    };

    // فهرست و جست‌وجو

    private static async Task<IResult> ListAsync(
        WarehouseDbContext db,
        string? q,
        EquipmentType? type,
        EquipmentStatus? status,
        Guid? ownerUnitId,
        Guid? buildingId,
        Guid? floorId,
        Guid? locationUnitId,
        bool noAssetNumber = false,
        bool includeRetired = false,
        int page = 1,
        int pageSize = DefaultPageSize,
        string? sort = null,
        bool desc = false)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = db.Equipment.AsNoTracking();

        // هر کلمه باید جایی پیدا شود. مقایسه فقط روی ستون یکدست‌شده است، هرگز روی متن فارسی خام.
        foreach (var word in (TextNormalizer.SearchKey(q) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Where(e => e.SearchText.Contains(word));
        }

        if (type is { } t) query = query.Where(e => e.Type == t);
        if (ownerUnitId is { } owner) query = query.Where(e => e.OwnerUnitId == owner);
        if (buildingId is { } building) query = query.Where(e => e.BuildingId == building);
        if (floorId is { } floor) query = query.Where(e => e.FloorId == floor);
        if (locationUnitId is { } location) query = query.Where(e => e.LocationUnitId == location);
        if (noAssetNumber) query = query.Where(e => e.HasTemporaryAssetNumber);

        if (status is { } s)
        {
            query = query.Where(e => e.Status == s);
        }
        else if (!includeRetired)
        {
            query = query.Where(e => e.Status != EquipmentStatus.Retired);
        }

        var total = await query.CountAsync();

        var items = await Sorted(query, sort, desc)
            .ThenBy(e => e.AssetNumberKey)
            .ThenBy(e => e.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToListItem)
            .ToListAsync();

        return Results.Ok(new PagedResult<EquipmentListItem> { Items = items, Total = total, Page = page, PageSize = pageSize });
    }

    // روی سرور، تا ترتیب روی همه‌ی صفحه‌ها درست باشد. مقدار خالی همیشه آخر می‌آید.
    private static IOrderedQueryable<Equipment> Sorted(IQueryable<Equipment> query, string? sort, bool desc)
    {
        IOrderedQueryable<Equipment> By<TKey>(Expression<Func<Equipment, TKey>> key) =>
            desc ? query.OrderByDescending(key) : query.OrderBy(key);

        IOrderedQueryable<Equipment> Then<TKey>(IOrderedQueryable<Equipment> first, Expression<Func<Equipment, TKey>> key) =>
            desc ? first.ThenByDescending(key) : first.ThenBy(key);

        return sort switch
        {
            EquipmentSort.Device => Then(Then(query.OrderBy(e => e.Brand == null && e.Model == null), e => e.Brand), e => e.Model),
            EquipmentSort.ComputerName => Then(query.OrderBy(e => e.ComputerNameKey == null), e => e.ComputerNameKey),
            EquipmentSort.Location => Then(By(e => e.LocationUnit!.Name), e => e.Room),
            EquipmentSort.Owner => By(e => e.OwnerUnit!.Name),
            EquipmentSort.Status => By(e => e.Status),
            EquipmentSort.Updated => By(e => e.UpdatedAtUtc),
            _ => By(e => e.AssetNumberKey)
        };
    }

    private static async Task<IResult> SummaryAsync(WarehouseDbContext db)
    {
        var counts = await db.Equipment.AsNoTracking()
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Count(EquipmentStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        return Results.Ok(new EquipmentStats
        {
            Total = counts.Where(c => c.Status != EquipmentStatus.Retired).Sum(c => c.Count),
            Active = Count(EquipmentStatus.Active),
            InStorage = Count(EquipmentStatus.InStorage),
            InRepair = Count(EquipmentStatus.InRepair),
            Retired = Count(EquipmentStatus.Retired),
            NoAssetNumber = await db.Equipment.AsNoTracking()
                .CountAsync(e => e.HasTemporaryAssetNumber && e.Status != EquipmentStatus.Retired)
        });
    }

    private static async Task<IResult> LookupAsync(WarehouseDbContext db, string? assetNumber, string? computerName)
    {
        var assetKey = TextNormalizer.Key(assetNumber);
        var nameKey = TextNormalizer.Key(computerName);

        if (assetKey is null && nameKey is null)
        {
            return ApiProblems.Validation("assetNumber", "شماره اموال یا نام کامپیوتر را بدهید.");
        }

        var query = db.Equipment.AsNoTracking();
        query = assetKey is not null
            ? query.Where(e => e.AssetNumberKey == assetKey)
            : query.Where(e => e.ComputerNameKey == nameKey && e.Status != EquipmentStatus.Retired);

        return Results.Ok(await query.Select(ToListItem).ToListAsync());
    }

    // جزئیات

    private static async Task<IResult> GetAsync(Guid id, WarehouseDbContext db)
    {
        var detail = await LoadDetailAsync(db, id, []);
        return detail is null ? ApiProblems.NotFound() : Results.Ok(detail);
    }

    private static async Task<EquipmentDetail?> LoadDetailAsync(WarehouseDbContext db, Guid id, IReadOnlyList<string> warnings)
    {
        var e = await db.Equipment.AsNoTracking()
            .Include(x => x.OwnerUnit)
            .Include(x => x.Building)
            .Include(x => x.Floor)
            .Include(x => x.LocationUnit)
            .Include(x => x.Components.OrderBy(c => c.Position))
            .FirstOrDefaultAsync(x => x.Id == id);

        if (e is null) return null;

        var parent = e.ParentId is { } parentId
            ? await db.Equipment.AsNoTracking().Where(x => x.Id == parentId).Select(ToListItem).FirstOrDefaultAsync()
            : null;

        var children = await db.Equipment.AsNoTracking()
            .Where(x => x.ParentId == id)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.AssetNumberKey)
            .Select(ToListItem)
            .ToListAsync();

        return new EquipmentDetail
        {
            Id = e.Id,
            Type = e.Type,
            Status = e.Status,
            AssetNumber = e.AssetNumber,
            HasTemporaryAssetNumber = e.HasTemporaryAssetNumber,
            SerialNumber = e.SerialNumber,
            Brand = e.Brand,
            Model = e.Model,
            SizeText = e.SizeText,
            ComputerName = e.ComputerName,
            PeripheralKind = e.PeripheralKind,
            HasMouse = e.HasMouse,
            HasKeyboard = e.HasKeyboard,
            OtherAccessories = e.OtherAccessories,
            OwnerUnitId = e.OwnerUnitId,
            OwnerUnitName = e.OwnerUnit!.Name,
            BuildingId = e.BuildingId,
            BuildingName = e.Building!.Name,
            FloorId = e.FloorId,
            FloorName = e.Floor!.Name,
            LocationUnitId = e.LocationUnitId,
            LocationUnitName = e.LocationUnit!.Name,
            Room = e.Room,
            Station = e.Station,
            StationPosition = e.StationPosition,
            Notes = e.Notes,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc,
            MouseModel = e.MouseModel,
            KeyboardModel = e.KeyboardModel,
            LastVerifiedAtUtc = e.LastVerifiedAtUtc,
            Version = e.Version,
            Components = e.Components
                .Select(c => new ComponentItem { Id = c.Id, Kind = c.Kind, Brand = c.Brand, Model = c.Model, Capacity = c.Capacity })
                .ToList(),
            Parent = parent,
            Children = children,
            Warnings = warnings
        };
    }

    // ثبت و ویرایش

    private static async Task<IResult> CreateAsync(EquipmentInput input, WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        if ((input.LinkChildIds.Count > 0 || input.LinkParentId is not null)
            && !await permissions.HasAsync(audit.UserId, Permissions.EquipmentLink))
        {
            return ApiProblems.Forbidden("اجازه‌ی اتصال دستگاه را ندارید.");
        }

        var errors = EquipmentRules.Validate(input);
        if (errors.Count > 0) return ApiProblems.Validation(errors);

        var referenceError = await CheckReferencesAsync(db, input, null);
        if (referenceError is not null) return ApiProblems.Validation([referenceError]);

        var equipment = new Equipment
        {
            Id = Guid.NewGuid(),
            Type = input.Type,
            CreatedAtUtc = DateTime.UtcNow,
            BuildingId = input.BuildingId,
            FloorId = input.FloorId,
            LocationUnitId = input.LocationUnitId,
            Room = TextNormalizer.Clean(input.Room),
            Station = TextNormalizer.Clean(input.Station),
            StationPosition = TextNormalizer.Clean(input.StationPosition)
        };
        ApplySpecs(equipment, input);
        await ApplyAssetNumberAsync(db, equipment, input);

        var duplicate = await FindDuplicateAsync(db, equipment.AssetNumberKey, null);
        if (duplicate is not null) return duplicate;

        var linkError = await ApplyLinksAsync(db, equipment, input);
        if (linkError is not null) return ApiProblems.Validation("Link", linkError);

        db.Equipment.Add(equipment);
        ReplaceComponents(db, equipment, input.Components);

        var warnings = await ComputerNameWarningsAsync(db, equipment);
        return await SaveAndReturnAsync(db, equipment, warnings);
    }

    // ثبت چندتایی: همه یا هیچ، در یک ذخیره و یک گروه تغییر. خطا با کلید «ردیف:فیلد» برمی‌گردد تا کنار همان ردیف بیاید.
    private static async Task<IResult> CreateBulkAsync(
        BulkCreateRequest request, WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        if (request.Items.Count is < BulkRules.MinCount or > BulkRules.MaxCount)
        {
            return ApiProblems.Validation("Items", $"تعداد باید بین {BulkRules.MinCount} و {BulkRules.MaxCount} باشد.");
        }

        if (request.Items.Any(i => i.LinkChildIds.Count > 0))
        {
            return ApiProblems.Validation("Items", "در ثبت چندتایی فقط می‌شود به یک مرکز وصل کرد.");
        }

        if (request.Items.Any(i => i.LinkParentId is not null)
            && !await permissions.HasAsync(audit.UserId, Permissions.EquipmentLink))
        {
            return ApiProblems.Forbidden("اجازه‌ی اتصال دستگاه را ندارید.");
        }

        var errors = new Dictionary<string, string[]>();
        void Fail(int row, string field, string message) => errors[$"{row}:{field}"] = [message];

        var devices = new List<Equipment>();
        var seenAssets = new HashSet<string>();
        var temporaryOffset = 0;
        var now = DateTime.UtcNow;

        for (var row = 0; row < request.Items.Count; row++)
        {
            var input = request.Items[row];

            var problems = EquipmentRules.Validate(input);
            foreach (var group in problems.GroupBy(p => p.Field))
            {
                errors[$"{row}:{group.Key}"] = group.Select(p => p.Message).ToArray();
            }

            if (problems.Count > 0) continue;

            var referenceError = await CheckReferencesAsync(db, input, null);
            if (referenceError is not null)
            {
                Fail(row, referenceError.Field, referenceError.Message);
                continue;
            }

            var equipment = new Equipment
            {
                Id = Guid.NewGuid(),
                Type = input.Type,
                CreatedAtUtc = now,
                BuildingId = input.BuildingId,
                FloorId = input.FloorId,
                LocationUnitId = input.LocationUnitId,
                Room = TextNormalizer.Clean(input.Room),
                Station = TextNormalizer.Clean(input.Station),
                StationPosition = TextNormalizer.Clean(input.StationPosition)
            };

            equipment.HasTemporaryAssetNumber = input.UseTemporaryAssetNumber;
            equipment.AssetNumber = input.UseTemporaryAssetNumber
                ? await NextTemporaryAssetNumberAsync(db, temporaryOffset++)
                : TextNormalizer.Clean(input.AssetNumber);
            equipment.AssetNumberKey = TextNormalizer.Key(equipment.AssetNumber);
            ApplySpecs(equipment, input);

            if (!seenAssets.Add(equipment.AssetNumberKey!))
            {
                Fail(row, nameof(input.AssetNumber), $"شماره اموال {equipment.AssetNumber} در همین فهرست تکراری است.");
                continue;
            }

            if (await db.Equipment.AsNoTracking().AnyAsync(e => e.AssetNumberKey == equipment.AssetNumberKey))
            {
                Fail(row, nameof(input.AssetNumber), $"شماره اموال {equipment.AssetNumber} قبلاً ثبت شده است.");
                continue;
            }

            var linkError = await ApplyLinksAsync(db, equipment, input);
            if (linkError is not null)
            {
                Fail(row, "Link", linkError);
                continue;
            }

            devices.Add(equipment);
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors, title: "چند ردیف نیاز به اصلاح دارد؛ چیزی ثبت نشد.");
        }

        var warnings = new List<string>();
        var seenNames = new HashSet<string>();
        foreach (var (equipment, input) in devices.Zip(request.Items))
        {
            db.Equipment.Add(equipment);
            ReplaceComponents(db, equipment, input.Components);

            if (equipment.ComputerNameKey is { } nameKey && !seenNames.Add(nameKey))
            {
                warnings.Add($"نام کامپیوتر «{equipment.ComputerName}» در این فهرست تکراری است.");
            }

            warnings.AddRange(await ComputerNameWarningsAsync(db, equipment));
        }

        audit.Reason = "ثبت چندتایی";

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            return DuplicateProblem(null, null);
        }

        return Results.Ok(new BulkCreateResponse { Created = devices.Count, Warnings = warnings });
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateEquipmentRequest request, WarehouseDbContext db)
    {
        var equipment = await db.Equipment.Include(e => e.Components).FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();

        // نوع، محل و وضعیت از این مسیر عوض نمی‌شوند؛ هرچه Client فرستاده باشد نادیده گرفته می‌شود.
        request.Type = equipment.Type;
        request.BuildingId = equipment.BuildingId;
        request.FloorId = equipment.FloorId;
        request.LocationUnitId = equipment.LocationUnitId;
        request.Room = equipment.Room;
        request.Station = equipment.Station;
        request.StationPosition = equipment.StationPosition;

        var errors = EquipmentRules.Validate(request);
        if (errors.Count > 0) return ApiProblems.Validation(errors);

        var referenceError = await CheckReferencesAsync(db, request, equipment);
        if (referenceError is not null) return ApiProblems.Validation([referenceError]);

        var duplicate = await FindDuplicateAsync(db, TextNormalizer.Key(request.AssetNumber), id);
        if (duplicate is not null) return duplicate;

        // اتصال هنگام ویرایش از مسیرهای link و unlink است، نه از این مسیر.
        ApplySpecs(equipment, request);
        await ApplyAssetNumberAsync(db, equipment, request);
        ReplaceComponents(db, equipment, request.Components);
        db.ExpectVersion(equipment, request.Version);

        var warnings = await ComputerNameWarningsAsync(db, equipment);
        return await SaveAndReturnAsync(db, equipment, warnings);
    }

    // جابه‌جایی

    private static async Task<IResult> MoveAsync(Guid id, MoveEquipmentRequest request, WarehouseDbContext db, AuditContext audit)
    {
        var equipment = await db.Equipment.Include(e => e.Children).FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();

        if (equipment.Status == EquipmentStatus.Retired)
        {
            return ApiProblems.Validation("Status", "دستگاه اسقاطی جابه‌جا نمی‌شود.");
        }

        var locationError = await CheckLocationAsync(db, request.BuildingId, request.FloorId, request.LocationUnitId, equipment);
        if (locationError is not null) return ApiProblems.Validation([locationError]);

        var lengthError = CheckLength(request.Room, EquipmentRules.MaxRoom, "Room", "اتاق")
                          ?? CheckLength(request.Station, EquipmentRules.MaxStation, "Station", "استیشن")
                          ?? CheckLength(request.StationPosition, EquipmentRules.MaxStationPosition, "StationPosition", "جای دقیق")
                          ?? CheckLength(request.Reason, EquipmentRules.MaxReason, "Reason", "دلیل");
        if (lengthError is not null) return ApiProblems.Validation([lengthError]);

        audit.Reason = TextNormalizer.Clean(request.Reason);

        ApplyLocation(equipment, request);
        db.ExpectVersion(equipment, request.Version);

        // هر فرزند ردیف تاریخچه‌ی خودش را می‌گیرد؛ همه در یک ChangeSet.
        if (request.MoveLinked)
        {
            foreach (var child in equipment.Children) ApplyLocation(child, request);
        }

        return await SaveAndReturnAsync(db, equipment, []);
    }

    private static Guid IdOf(string? value)
    {
        if (value is null) return Guid.Empty;
        var pipe = value.IndexOf('|');
        return Guid.TryParse(pipe > 0 ? value[..pipe] : value, out var g) ? g : Guid.Empty;
    }

    // انبارگردانی

    private static async Task<IResult> VerifyAsync(Guid id, VerifyRequest request, WarehouseDbContext db)
    {
        var equipment = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();
        if (equipment.Status == EquipmentStatus.Retired) return ApiProblems.Validation("Status", "دستگاه اسقاطی در انبارگردانی تأیید نمی‌شود.");

        equipment.LastVerifiedAtUtc = DateTime.UtcNow;
        db.ExpectVersion(equipment, request.Version);
        return await SaveAndReturnAsync(db, equipment, []);
    }

    // تعمیرات

    private static RepairItem ToRepairItem(RepairRecord r) => new()
    {
        Id = r.Id, SentAtUtc = r.SentAtUtc, Vendor = r.Vendor, Problem = r.Problem, ReturnedAtUtc = r.ReturnedAtUtc,
        Result = r.Result, CostRials = r.CostRials, Version = r.Version
    };

    private static async Task<IResult> RepairsAsync(Guid id, WarehouseDbContext db)
    {
        if (!await db.Equipment.AsNoTracking().AnyAsync(e => e.Id == id)) return ApiProblems.NotFound();

        var repairs = await db.Repairs.AsNoTracking().Where(r => r.EquipmentId == id).OrderByDescending(r => r.SentAtUtc).ToListAsync();
        return Results.Ok(repairs.Select(ToRepairItem).ToList());
    }

    private static IResult? CheckRepairText(string? vendor, string? problem, string? result, long? cost)
    {
        var error = CheckLength(vendor, RepairRules.MaxVendor, "Vendor", "تعمیرگاه")
                    ?? CheckLength(problem, RepairRules.MaxProblem, "Problem", "مشکل")
                    ?? CheckLength(result, RepairRules.MaxResult, "Result", "نتیجه");
        if (error is not null) return ApiProblems.Validation([error]);
        if (cost is < 0) return ApiProblems.Validation("CostRials", "هزینه منفی نمی‌شود.");
        return null;
    }

    // ارسال به تعمیر: یک ردیف تعمیر و وضعیت «در تعمیر»، در یک ChangeSet.
    private static async Task<IResult> SendToRepairAsync(Guid id, RepairSendRequest request, WarehouseDbContext db)
    {
        var equipment = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();
        if (equipment.Status == EquipmentStatus.Retired) return ApiProblems.Validation("Status", "دستگاه اسقاطی به تعمیر نمی‌رود.");

        var problem = TextNormalizer.Clean(request.Problem);
        if (problem is null) return ApiProblems.Validation("Problem", "مشکل دستگاه را بنویسید.");
        var vendor = TextNormalizer.Clean(request.Vendor);
        if (CheckRepairText(vendor, problem, null, null) is { } textError) return textError;

        if (await db.Repairs.AnyAsync(r => r.EquipmentId == id && r.ReturnedAtUtc == null))
        {
            return ApiProblems.Validation("Problem", "این دستگاه هنوز از تعمیر قبلی برنگشته است؛ اول برگشتش را ثبت کنید.");
        }

        var sent = request.SentAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        if (sent > DateTime.UtcNow.AddMinutes(5)) return ApiProblems.Validation("SentAtUtc", "تاریخ ارسال در آینده است.");

        db.Repairs.Add(new RepairRecord { Id = Guid.NewGuid(), EquipmentId = id, SentAtUtc = sent, Vendor = vendor, Problem = problem });
        equipment.Status = EquipmentStatus.InRepair;
        db.ExpectVersion(equipment, request.EquipmentVersion);
        return await SaveAndReturnAsync(db, equipment, []);
    }

    private static async Task<IResult> ReturnFromRepairAsync(Guid repairId, RepairReturnRequest request, WarehouseDbContext db)
    {
        var repair = await db.Repairs.FirstOrDefaultAsync(r => r.Id == repairId);
        if (repair is null) return ApiProblems.NotFound();
        if (repair.ReturnedAtUtc is not null) return ApiProblems.Validation("ReturnedAtUtc", "برگشت این تعمیر قبلاً ثبت شده است.");
        if (request.NewStatus is not (EquipmentStatus.Active or EquipmentStatus.InStorage))
        {
            return ApiProblems.Validation("NewStatus", "بعد از تعمیر، دستگاه «در حال استفاده» یا «در انبار» است. برای اسقاط، وضعیت را جدا تغییر دهید.");
        }

        var result = TextNormalizer.Clean(request.Result);
        if (CheckRepairText(null, null, result, request.CostRials) is { } textError) return textError;

        var returned = request.ReturnedAtUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        if (returned < repair.SentAtUtc) return ApiProblems.Validation("ReturnedAtUtc", "تاریخ برگشت پیش از تاریخ ارسال است.");

        var equipment = await db.Equipment.FirstAsync(e => e.Id == repair.EquipmentId);
        repair.ReturnedAtUtc = returned;
        repair.Result = result;
        repair.CostRials = request.CostRials;
        db.ExpectVersion(repair, request.Version);
        equipment.Status = request.NewStatus;
        db.ExpectVersion(equipment, request.EquipmentVersion);
        return await SaveAndReturnAsync(db, equipment, []);
    }

    // پرس‌وجوی اصلی فهرست، فقط برای گرم کردن سرور بعد از روشن شدن.
    internal static async Task WarmUpAsync(WarehouseDbContext db, CancellationToken token)
    {
        await db.Equipment.AsNoTracking().OrderBy(e => e.AssetNumberKey).Take(50).Select(ToListItem).ToListAsync(token);
        await db.Equipment.AsNoTracking().CountAsync(e => e.Status != EquipmentStatus.Retired, token);
    }

    // کار گروهی

    private static IResult? CheckBulkItems(List<BulkItem>? items)
    {
        if (items is null || items.Count == 0) return ApiProblems.Validation("Items", "هیچ دستگاهی انتخاب نشده است.");
        if (items.Count > BulkActionRules.MaxItems) return ApiProblems.Validation("Items", $"حداکثر {BulkActionRules.MaxItems} دستگاه در هر بار.");
        if (items.Select(i => i.Id).Distinct().Count() != items.Count) return ApiProblems.Validation("Items", "دستگاه تکراری در انتخاب.");
        return null;
    }

    // همه یا هیچ: یک دستگاه نامعتبر یا تغییرکرده یعنی هیچ‌کدام ذخیره نمی‌شود.
    private static async Task<IResult> BulkMoveAsync(BulkMoveRequest request, WarehouseDbContext db, AuditContext audit)
    {
        if (CheckBulkItems(request.Items) is { } itemsError) return itemsError;

        var ids = request.Items.Select(i => i.Id).ToList();
        var devices = await db.Equipment.Include(e => e.Children).Where(e => ids.Contains(e.Id)).ToListAsync();
        if (devices.Count != ids.Count) return ApiProblems.NotFound();

        var retired = devices.Where(d => d.Status == EquipmentStatus.Retired).Select(d => d.AssetNumber).ToList();
        if (retired.Count > 0)
        {
            return ApiProblems.Validation("Items", $"دستگاه اسقاطی جابه‌جا نمی‌شود: {string.Join("، ", retired)}");
        }

        var locationError = await CheckLocationAsync(db, request.BuildingId, request.FloorId, request.LocationUnitId, null);
        if (locationError is not null) return ApiProblems.Validation([locationError]);

        var lengthError = CheckLength(request.Room, EquipmentRules.MaxRoom, "Room", "اتاق")
                          ?? CheckLength(request.Station, EquipmentRules.MaxStation, "Station", "استیشن")
                          ?? CheckLength(request.StationPosition, EquipmentRules.MaxStationPosition, "StationPosition", "جای دقیق")
                          ?? CheckLength(request.Reason, EquipmentRules.MaxReason, "Reason", "دلیل");
        if (lengthError is not null) return ApiProblems.Validation([lengthError]);

        audit.Reason = TextNormalizer.Clean(request.Reason);

        var location = new MoveEquipmentRequest
        {
            BuildingId = request.BuildingId, FloorId = request.FloorId, LocationUnitId = request.LocationUnitId,
            Room = request.Room, Station = request.Station, StationPosition = request.StationPosition
        };

        var versions = request.Items.ToDictionary(i => i.Id, i => i.Version);
        foreach (var device in devices)
        {
            ApplyLocation(device, location);
            db.ExpectVersion(device, versions[device.Id]);
            if (request.MoveLinked)
            {
                foreach (var child in device.Children) ApplyLocation(child, location);
            }
        }

        return await SaveBulkAsync(db, devices.Count);
    }

    private static async Task<IResult> BulkStatusAsync(BulkStatusRequest request, WarehouseDbContext db, AuditContext audit, PermissionService permissions)
    {
        if (CheckBulkItems(request.Items) is { } itemsError) return itemsError;
        if (!Enum.IsDefined(request.Status)) return ApiProblems.Validation("Status", "وضعیت نامعتبر است.");

        var reason = TextNormalizer.Clean(request.Reason);
        if (reason?.Length > EquipmentRules.MaxReason) return ApiProblems.Validation("Reason", $"دلیل حداکثر {EquipmentRules.MaxReason} نویسه است.");
        if (request.Status == EquipmentStatus.Retired && reason is null) return ApiProblems.Validation("Reason", "دلیل اسقاط را بنویسید.");

        var ids = request.Items.Select(i => i.Id).ToList();
        var devices = await db.Equipment.Include(e => e.Children).Where(e => ids.Contains(e.Id)).ToListAsync();
        if (devices.Count != ids.Count) return ApiProblems.NotFound();

        if (request.Status != EquipmentStatus.Retired
            && devices.Any(d => d.Status == EquipmentStatus.Retired)
            && !await permissions.HasAsync(audit.UserId, Permissions.EquipmentRestore))
        {
            return ApiProblems.Forbidden("اجازه‌ی بازگرداندن از اسقاط را ندارید.");
        }

        audit.Reason = reason;
        var versions = request.Items.ToDictionary(i => i.Id, i => i.Version);
        foreach (var device in devices)
        {
            device.Status = request.Status;
            db.ExpectVersion(device, versions[device.Id]);

            // همان قاعده‌ی تکی: اسقاط اتصال‌ها را قطع می‌کند.
            if (request.Status == EquipmentStatus.Retired)
            {
                device.ParentId = null;
                foreach (var child in device.Children) child.ParentId = null;
            }
        }

        return await SaveBulkAsync(db, devices.Count);
    }

    private static async Task<IResult> SaveBulkAsync(WarehouseDbContext db, int count)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(new BulkActionResult { Changed = count });
    }

    private static void ApplyLocation(Equipment target, MoveEquipmentRequest request)
    {
        target.BuildingId = request.BuildingId;
        target.FloorId = request.FloorId;
        target.LocationUnitId = request.LocationUnitId;
        target.Room = TextNormalizer.Clean(request.Room);
        target.Station = TextNormalizer.Clean(request.Station);
        target.StationPosition = TextNormalizer.Clean(request.StationPosition);
        target.SearchText = BuildSearchText(target);
    }

    // وضعیت

    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        ChangeStatusRequest request,
        WarehouseDbContext db,
        AuditContext audit,
        PermissionService permissions)
    {
        if (!Enum.IsDefined(request.Status)) return ApiProblems.Validation("Status", "وضعیت نامعتبر است.");

        var equipment = await db.Equipment.Include(e => e.Children).FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();

        var reason = TextNormalizer.Clean(request.Reason);

        if (reason?.Length > EquipmentRules.MaxReason)
        {
            return ApiProblems.Validation("Reason", $"دلیل حداکثر {EquipmentRules.MaxReason} نویسه است.");
        }

        if (request.Status == EquipmentStatus.Retired && reason is null)
        {
            return ApiProblems.Validation("Reason", "دلیل اسقاط را بنویسید.");
        }

        if (equipment.Status == EquipmentStatus.Retired
            && request.Status != EquipmentStatus.Retired
            && !await permissions.HasAsync(audit.UserId, Permissions.EquipmentRestore))
        {
            return ApiProblems.Forbidden("اجازه‌ی بازگرداندن از اسقاط را ندارید.");
        }

        audit.Reason = reason;
        equipment.Status = request.Status;
        db.ExpectVersion(equipment, request.Version);

        // اسقاط: مرکز فرزندانش را رها می‌کند و فرزند خودش جدا می‌شود؛ هر کدام با تاریخچه.
        if (request.Status == EquipmentStatus.Retired)
        {
            equipment.ParentId = null;
            foreach (var child in equipment.Children) child.ParentId = null;
        }

        return await SaveAndReturnAsync(db, equipment, []);
    }

    // اتصال

    private static async Task<IResult> LinkAsync(Guid id, LinkRequest request, WarehouseDbContext db)
    {
        var hub = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id);
        if (hub is null) return ApiProblems.NotFound();

        var child = await db.Equipment.FirstOrDefaultAsync(e => e.Id == request.ChildId);
        if (child is null) return ApiProblems.Validation("ChildId", "دستگاه انتخاب‌شده پیدا نشد.");

        if (!EquipmentRules.IsHub(hub.Type))
        {
            return ApiProblems.Validation("ChildId", "مرکز مجموعه فقط کیس یا لپ‌تاپ است.");
        }

        if (EquipmentRules.IsHub(child.Type))
        {
            return ApiProblems.Validation("ChildId", "کیس و لپ‌تاپ را نمی‌شود به دستگاه دیگری وصل کرد.");
        }

        if (hub.Status == EquipmentStatus.Retired || child.Status == EquipmentStatus.Retired)
        {
            return ApiProblems.Validation("ChildId", "دستگاه اسقاطی وصل نمی‌شود.");
        }

        if (child.ParentId is not null)
        {
            return ApiProblems.Validation("ChildId", "این دستگاه به مرکز دیگری وصل است؛ اول آن را جدا کنید.");
        }

        child.ParentId = hub.Id;
        db.ExpectVersion(hub, request.Version);

        return await SaveAndReturnAsync(db, hub, []);
    }

    private static async Task<IResult> UnlinkAsync(Guid id, LinkRequest request, WarehouseDbContext db)
    {
        var hub = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id);
        if (hub is null) return ApiProblems.NotFound();

        var child = await db.Equipment.FirstOrDefaultAsync(e => e.Id == request.ChildId);
        if (child is null || child.ParentId != hub.Id)
        {
            return ApiProblems.Validation("ChildId", "این دستگاه به این مرکز وصل نیست.");
        }

        child.ParentId = null;
        db.ExpectVersion(hub, request.Version);

        return await SaveAndReturnAsync(db, hub, []);
    }

    // حذف: فقط دستگاهی که به هیچ مجموعه‌ای وصل نیست. قطعات مال خود دستگاه‌اند و با آن می‌روند؛ تاریخچه می‌ماند.

    private static async Task<IResult> DependenciesAsync(Guid id, WarehouseDbContext db)
    {
        if (!await db.Equipment.AsNoTracking().AnyAsync(e => e.Id == id)) return ApiProblems.NotFound();

        return Results.Ok(await FindDependenciesAsync(db, id));
    }

    private static async Task<DependencyList> FindDependenciesAsync(WarehouseDbContext db, Guid id)
    {
        var parent = await db.Equipment.AsNoTracking()
            .Where(e => e.Id == id && e.Parent != null)
            .Select(e => new { e.Parent!.Id, e.Parent.Type, e.Parent.AssetNumber })
            .FirstOrDefaultAsync();

        var children = await db.Equipment.AsNoTracking()
            .Where(e => e.ParentId == id)
            .Select(e => new { e.Id, e.Type, e.AssetNumber })
            .ToListAsync();

        var items = new List<DependencyItem>();
        if (parent is not null)
        {
            items.Add(new DependencyItem("متصل به", $"{AuditLabels.Type(parent.Type)} {parent.AssetNumber}", parent.Id));
        }

        items.AddRange(children.Select(c =>
            new DependencyItem("دستگاه متصل", $"{AuditLabels.Type(c.Type)} {c.AssetNumber}", c.Id)));

        // سابقه‌ی تعمیر مثل تاریخچه است و پاک نمی‌شود؛ دستگاهی که آن را دارد حذف نمی‌شود، اسقاط می‌شود.
        var repairs = await db.Repairs.AsNoTracking().CountAsync(r => r.EquipmentId == id);
        if (repairs > 0)
        {
            items.Add(new DependencyItem("سابقه‌ی تعمیر", $"{JalaliDate.ToPersianDigits(repairs.ToString())} بار ارسال به تعمیر؛ به‌جای حذف، اسقاط کنید", null));
        }

        return new DependencyList { Items = items, Total = items.Count };
    }

    private static async Task<IResult> DeleteAsync(Guid id, WarehouseDbContext db)
    {
        var equipment = await db.Equipment.Include(e => e.Components).FirstOrDefaultAsync(e => e.Id == id);
        if (equipment is null) return ApiProblems.NotFound();

        if (!(await FindDependenciesAsync(db, id)).CanDelete)
        {
            return ApiProblems.Conflict("این دستگاه به دستگاه دیگری وصل است؛ اول اتصال را قطع کنید.");
        }

        db.Components.RemoveRange(equipment.Components);
        db.Equipment.Remove(equipment);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }

        return Results.Ok(new DependencyList());
    }

    // تاریخچه

    private static async Task<IResult> HistoryAsync(Guid id, WarehouseDbContext db)
    {
        if (!await db.Equipment.AsNoTracking().AnyAsync(e => e.Id == id)) return ApiProblems.NotFound();

        // اتصال و جدا شدن روی فرزند ثبت می‌شود (ParentId مال فرزند است)؛ آن ردیف‌ها در تاریخچه‌ی مرکز هم می‌آیند.
        // مقدار به شکل «شناسه|شماره» ذخیره شده؛ مقایسه روی شناسه‌ی ASCII است، نه متن فارسی.
        var idText = id.ToString();
        var rows = await db.AuditEntries.AsNoTracking()
            .Where(a => a.EquipmentId == id
                        || (a.Field == "ParentId" && ((a.OldValue != null && a.OldValue.StartsWith(idText))
                                                      || (a.NewValue != null && a.NewValue.StartsWith(idText)))))
            .OrderByDescending(a => a.AtUtc)
            .ToListAsync();

        // شناسه‌ها به «نوع شماره»: دستگاه‌های دیگرِ این ردیف‌ها و مقدارهای «متصل به».
        var ids = rows.Where(a => a.EquipmentId != id).Select(a => a.EquipmentId ?? a.EntityId)
            .Concat(rows.Where(a => a.Field == "ParentId").SelectMany(a => new[] { a.OldValue, a.NewValue }).Select(IdOf))
            .Where(g => g != Guid.Empty)
            .Distinct()
            .ToList();

        var names = (await db.Equipment.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.Type, e.AssetNumber })
                .ToListAsync())
            .ToDictionary(e => e.Id, e => $"{AuditLabels.Type(e.Type)} {e.AssetNumber}");

        // دستگاه حذف‌شده: همان شماره‌ای که هنگام ثبت ردیف در مقدار نوشته شده.
        string? Name(string? value)
        {
            if (value is null) return null;
            var g = IdOf(value);
            if (g == Guid.Empty) return value;
            var pipe = value.IndexOf('|');
            return names.GetValueOrDefault(g, pipe > 0 ? $"دستگاه {value[(pipe + 1)..]}" : "دستگاه حذف‌شده");
        }

        var items = rows.Select(a => new AuditEntryItem
        {
            Id = a.Id,
            ChangeSetId = a.ChangeSetId,
            AtUtc = a.AtUtc,
            UserName = a.UserName,
            EntityType = a.EntityType,
            EntityLabel = a.EntityLabel,
            Action = a.Action,
            Field = a.Field,
            OldValue = a.OldValue,
            NewValue = a.NewValue,
            Reason = a.Reason,
            Subject = a.EquipmentId != id ? names.GetValueOrDefault(a.EquipmentId ?? a.EntityId, "دستگاه حذف‌شده") : null,
            OldText = a.Field == "ParentId" ? Name(a.OldValue) : null,
            NewText = a.Field == "ParentId" ? Name(a.NewValue) : null
        }).ToList();

        return Results.Ok(items);
    }

    // ابزارها

    internal static void ApplySpecs(Equipment target, EquipmentInput input)
    {
        target.SerialNumber = TextNormalizer.Clean(input.SerialNumber);
        target.Brand = TextNormalizer.Clean(input.Brand);
        target.Model = TextNormalizer.Clean(input.Model);
        target.SizeText = TextNormalizer.Clean(input.SizeText);
        target.ComputerName = TextNormalizer.Clean(input.ComputerName);
        target.ComputerNameKey = TextNormalizer.Key(input.ComputerName);
        target.PeripheralKind = TextNormalizer.Clean(input.PeripheralKind);
        target.HasMouse = input.HasMouse;
        target.HasKeyboard = input.HasKeyboard;
        target.MouseModel = input.HasMouse ? TextNormalizer.Clean(input.MouseModel) : null;
        target.KeyboardModel = input.HasKeyboard ? TextNormalizer.Clean(input.KeyboardModel) : null;
        target.OtherAccessories = TextNormalizer.Clean(input.OtherAccessories);
        target.OwnerUnitId = input.OwnerUnitId;
        target.Notes = TextNormalizer.Clean(input.Notes);
        target.SearchText = BuildSearchText(target);
    }

    internal static string BuildSearchText(Equipment e) =>
        string.Join('|', new[]
            {
                e.AssetNumber, e.SerialNumber, e.ComputerName, e.Brand, e.Model, e.PeripheralKind, e.Room, e.Station
            }
            .Select(TextNormalizer.SearchKey)
            .Where(v => v is not null));

    // شماره‌ی موقت فقط یک بار ساخته می‌شود؛ ویرایش بعدی همان را نگه می‌دارد تا شماره‌ی واقعی وارد شود.
    private static async Task ApplyAssetNumberAsync(WarehouseDbContext db, Equipment target, EquipmentInput input)
    {
        if (input.UseTemporaryAssetNumber)
        {
            if (!target.HasTemporaryAssetNumber || target.AssetNumber is null)
            {
                target.AssetNumber = await NextTemporaryAssetNumberAsync(db);
                target.AssetNumberKey = TextNormalizer.Key(target.AssetNumber);
            }

            target.HasTemporaryAssetNumber = true;
        }
        else
        {
            target.AssetNumber = TextNormalizer.Clean(input.AssetNumber);
            target.AssetNumberKey = TextNormalizer.Key(input.AssetNumber);
            target.HasTemporaryAssetNumber = false;
        }

        target.SearchText = BuildSearchText(target);
    }

    internal static async Task<string> NextTemporaryAssetNumberAsync(WarehouseDbContext db, int offset = 0)
    {
        var existing = await db.Equipment.AsNoTracking()
            .Where(e => e.AssetNumberKey != null && e.AssetNumberKey.StartsWith(EquipmentRules.TemporaryAssetPrefix))
            .Select(e => e.AssetNumberKey!)
            .ToListAsync();

        var highest = existing
            .Select(k => int.TryParse(k[EquipmentRules.TemporaryAssetPrefix.Length..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{EquipmentRules.TemporaryAssetPrefix}{highest + offset + 1:000000}";
    }

    // اتصال همان موقع ثبت. همان قواعد مسیر link؛ خطا یعنی چیزی ذخیره نمی‌شود.
    private static async Task<string?> ApplyLinksAsync(WarehouseDbContext db, Equipment equipment, EquipmentInput input)
    {
        if (input.LinkParentId is { } parentId)
        {
            var parent = await db.Equipment.AsNoTracking().FirstOrDefaultAsync(e => e.Id == parentId);
            if (parent is null) return "مرکز انتخاب‌شده پیدا نشد.";
            if (!EquipmentRules.IsHub(parent.Type)) return "مرکز مجموعه فقط کیس یا لپ‌تاپ است.";
            if (parent.Status == EquipmentStatus.Retired) return "دستگاه اسقاطی وصل نمی‌شود.";

            equipment.ParentId = parentId;
        }

        foreach (var childId in input.LinkChildIds.Distinct())
        {
            var child = await db.Equipment.FirstOrDefaultAsync(e => e.Id == childId);
            if (child is null) return "دستگاه انتخاب‌شده پیدا نشد.";
            if (EquipmentRules.IsHub(child.Type)) return "کیس و لپ‌تاپ را نمی‌شود به دستگاه دیگری وصل کرد.";
            if (child.Status == EquipmentStatus.Retired) return "دستگاه اسقاطی وصل نمی‌شود.";
            if (child.ParentId is not null) return $"دستگاه {child.AssetNumber} به مرکز دیگری وصل است.";

            child.ParentId = equipment.Id;

            if (input.MoveLinkedToSameLocation)
            {
                child.BuildingId = equipment.BuildingId;
                child.FloorId = equipment.FloorId;
                child.LocationUnitId = equipment.LocationUnitId;
                child.Room = equipment.Room;
                child.Station = equipment.Station;
                child.StationPosition = equipment.StationPosition;
                child.SearchText = BuildSearchText(child);
            }
        }

        return null;
    }

    // فهرست کامل جایگزین می‌شود: با شناسه ← همان قطعه؛ بی‌شناسه ← جدید؛ جامانده ← حذف با تاریخچه.
    private static void ReplaceComponents(WarehouseDbContext db, Equipment equipment, IReadOnlyList<ComponentInput> inputs)
    {
        var existing = equipment.Components.ToDictionary(c => c.Id);
        var kept = new HashSet<Guid>();
        var position = 0;

        foreach (var input in inputs)
        {
            Component component;
            if (input.Id is { } id && existing.TryGetValue(id, out var found))
            {
                component = found;
                kept.Add(id);
            }
            else
            {
                component = new Component { Id = Guid.NewGuid(), EquipmentId = equipment.Id };
                db.Components.Add(component);
            }

            component.Kind = input.Kind;
            component.Brand = TextNormalizer.Clean(input.Brand);
            component.Model = TextNormalizer.Clean(input.Model);
            component.Capacity = TextNormalizer.Clean(input.Capacity);
            component.Position = position++;
        }

        foreach (var removed in existing.Values.Where(c => !kept.Contains(c.Id)))
        {
            db.Components.Remove(removed);
        }
    }

    private static async Task<IResult?> FindDuplicateAsync(WarehouseDbContext db, string? key, Guid? exceptId)
    {
        if (key is null) return null;

        var existing = await db.Equipment.AsNoTracking()
            .Where(e => e.AssetNumberKey == key && e.Id != exceptId)
            .Select(e => new { e.Id, e.Type, e.Brand, e.Model, e.Status })
            .FirstOrDefaultAsync();

        return existing is null ? null : DuplicateProblem(existing.Id, DescribeDuplicate(existing.Type, existing.Brand, existing.Model, existing.Status));
    }

    private static string DescribeDuplicate(EquipmentType type, string? brand, string? model, EquipmentStatus status)
    {
        var name = string.Join(' ', new[] { AuditLabels.Type(type), brand, model }.Where(v => !string.IsNullOrEmpty(v)));
        return status == EquipmentStatus.Retired ? $"{name} (اسقاط)" : name;
    }

    private static IResult DuplicateProblem(Guid? existingId, string? summary) =>
        Results.Problem(
            title: "این شماره اموال قبلاً ثبت شده است.",
            type: ProblemTypes.DuplicateAssetNumber,
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["existingId"] = existingId, ["existingSummary"] = summary });

    // فقط مقدارهای تازه باید فعال باشند؛ مقدار قبلی غیرفعال‌شده روی دستگاه موجود دست نمی‌خورد.
    private static async Task<ValidationError?> CheckReferencesAsync(WarehouseDbContext db, EquipmentInput input, Equipment? current)
    {
        if (current is null || current.OwnerUnitId != input.OwnerUnitId)
        {
            var owner = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == input.OwnerUnitId);
            if (owner is null) return new ValidationError(nameof(input.OwnerUnitId), "واحد صاحب اموال پیدا نشد.");
            if (!owner.IsActive) return new ValidationError(nameof(input.OwnerUnitId), "این واحد غیرفعال است.");
        }

        return await CheckLocationAsync(db, input.BuildingId, input.FloorId, input.LocationUnitId, current);
    }

    private static async Task<ValidationError?> CheckLocationAsync(
        WarehouseDbContext db, Guid buildingId, Guid floorId, Guid unitId, Equipment? current)
    {
        if (current is null || current.BuildingId != buildingId)
        {
            var building = await db.Buildings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == buildingId);
            if (building is null) return new ValidationError("BuildingId", "ساختمان را انتخاب کنید.");
            if (!building.IsActive) return new ValidationError("BuildingId", "این ساختمان غیرفعال است.");
        }

        var floor = await db.Floors.AsNoTracking().FirstOrDefaultAsync(f => f.Id == floorId);
        if (floor is null) return new ValidationError("FloorId", "طبقه را انتخاب کنید.");
        if (floor.BuildingId != buildingId) return new ValidationError("FloorId", "این طبقه متعلق به ساختمان انتخاب‌شده نیست.");
        if ((current is null || current.FloorId != floorId) && !floor.IsActive)
        {
            return new ValidationError("FloorId", "این طبقه غیرفعال است.");
        }

        if (current is null || current.LocationUnitId != unitId)
        {
            var unit = await db.Units.AsNoTracking().FirstOrDefaultAsync(u => u.Id == unitId);
            if (unit is null) return new ValidationError("LocationUnitId", "واحد محل را انتخاب کنید.");
            if (!unit.IsActive) return new ValidationError("LocationUnitId", "این واحد غیرفعال است.");
        }

        return null;
    }

    private static ValidationError? CheckLength(string? value, int max, string field, string label) =>
        TextNormalizer.Clean(value)?.Length > max ? new ValidationError(field, $"{label} حداکثر {max} نویسه است.") : null;

    // هشدار است، نه خطا: نام کامپیوتر تکراری بین دستگاه‌های غیراسقاط.
    private static async Task<IReadOnlyList<string>> ComputerNameWarningsAsync(WarehouseDbContext db, Equipment equipment)
    {
        if (equipment.ComputerNameKey is null) return [];

        var key = equipment.ComputerNameKey;
        var exists = await db.Equipment.AsNoTracking()
            .AnyAsync(e => e.ComputerNameKey == key && e.Id != equipment.Id && e.Status != EquipmentStatus.Retired);

        return exists ? [$"نام کامپیوتر «{equipment.ComputerName}» روی دستگاه دیگری هم ثبت شده است."] : [];
    }

    private static async Task<IResult> SaveAndReturnAsync(WarehouseDbContext db, Equipment equipment, IReadOnlyList<string> warnings)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiProblems.VersionConflict();
        }
        catch (DbUpdateException ex) when (ApiProblems.IsUniqueViolation(ex))
        {
            // دو ثبت هم‌زمان: unique index آخرین سد است.
            return DuplicateProblem(null, null);
        }

        return Results.Ok(await LoadDetailAsync(db, equipment.Id, warnings));
    }
}
