using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Assets;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Users;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Operations;

// فرمان‌های نگهداری: migrate و backup برای سرور، seed-demo فقط برای دیدن برنامه با داده‌ی ساختگی.
public static class OperationCommands
{
    public static async Task<int> MigrateAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Database.MigrateAsync();

        Console.WriteLine("Database is up to date.");
        return 0;
    }

    // VACUUM INTO نسخه‌ی سازگار می‌سازد؛ کپی ساده‌ی فایل در حال کار ممکن است خراب باشد.
    public static async Task<int> BackupAsync(WebApplication app, string[] args)
    {
        if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            Console.Error.WriteLine("Usage: backup <targetDir>");
            return 1;
        }

        const int keep = 30;
        var directory = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, $"emu-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WarehouseDbContext>();

        try
        {
            await VacuumIntoAsync(db, target);
        }
        catch (SqliteException ex)
        {
            Console.Error.WriteLine("Backup failed: " + ex.Message);
            return 1;
        }

        // نام فایل زمان را دارد، پس مرتب‌سازی نزولی یعنی جدیدترین اول.
        foreach (var old in Directory.GetFiles(directory, "emu-*.db").OrderDescending().Skip(keep))
        {
            File.Delete(old);
        }

        BackupMarker.Write(app.Services.GetRequiredService<DatabaseLocation>().FilePath, directory);
        Console.WriteLine($"Backup written: {target}");
        return 0;
    }

    public static async Task VacuumIntoAsync(WarehouseDbContext db, string target)
    {
        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();

        command.CommandText = "VACUUM INTO $target";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$target";
        parameter.Value = target;
        command.Parameters.Add(parameter);

        await command.ExecuteNonQueryAsync();
    }

    public static async Task<int> SeedDemoAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("seed-demo runs only in Development.");
            return 1;
        }

        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<WarehouseDbContext>();

        await db.Database.MigrateAsync();

        if (await db.Equipment.AnyAsync() || await db.Users.AnyAsync())
        {
            Console.Error.WriteLine("The database is not empty. Nothing was changed.");
            return 1;
        }


        var users = services.GetRequiredService<UserManager<AppUser>>();
        var admin = new AppUser { Id = Guid.NewGuid(), UserName = "demo-admin", DisplayName = "مدیر آزمایشی", IsActive = true };
        var password = CredentialsGenerator.NewPassword();
        var created = await users.CreateAsync(admin, password);
        if (!created.Succeeded) return Auth.CliConsole.Fail("create demo admin", created.Errors);
        await users.AddToRoleAsync(admin, Roles.Admin);

        Building AddBuilding(string name)
        {
            var building = new Building { Id = Guid.NewGuid(), Name = name, NameKey = TextNormalizer.Key(name)! };
            db.Buildings.Add(building);
            return building;
        }

        Floor AddFloor(Building building, string name, int order)
        {
            var floor = new Floor
            {
                Id = Guid.NewGuid(), BuildingId = building.Id, Name = name, NameKey = TextNormalizer.Key(name)!, SortOrder = order
            };
            db.Floors.Add(floor);
            return floor;
        }

        Unit AddUnit(string name)
        {
            var unit = new Unit { Id = Guid.NewGuid(), Name = name, NameKey = TextNormalizer.Key(name)! };
            db.Units.Add(unit);
            return unit;
        }

        var north = AddBuilding("ساختمان آزمایشی شمالی");
        var south = AddBuilding("ساختمان آزمایشی جنوبی");
        Floor[] floors =
        [
            AddFloor(north, "زیرزمین", -1), AddFloor(north, "همکف", 0), AddFloor(north, "طبقه 1", 1),
            AddFloor(north, "طبقه 2", 2), AddFloor(north, "طبقه 3", 3),
            AddFloor(south, "همکف", 0), AddFloor(south, "طبقه 1", 1), AddFloor(south, "طبقه 2", 2)
        ];
        Unit[] units =
        [
            AddUnit("واحد آزمایشی الف"), AddUnit("واحد آزمایشی ب"), AddUnit("واحد آزمایشی ج"), AddUnit("واحد آزمایشی د"),
            AddUnit("واحد آزمایشی ه"), AddUnit("واحد آزمایشی و"), AddUnit("واحد آزمایشی IT"), AddUnit("انبار آزمایشی")
        ];

        const int perType = 50;

        // ثابت، تا هر بار همان داده ساخته شود.
        var random = new Random(1405);
        T Pick<T>(params T[] values) => values[random.Next(values.Length)];

        var now = DateTime.UtcNow;
        var number = 1;
        var temporary = 0;

        Equipment AddDevice(EquipmentType type, int index, string prefix, string[] brands, Action<Equipment>? fill = null)
        {
            // هر دوازدهمین دستگاه شماره ندارد؛ وضعیت‌ها هم پخش‌اند تا زبانه‌ها و فیلترها امتحان شوند.
            var noAsset = index % 12 == 11;
            var asset = noAsset ? $"{EquipmentRules.TemporaryAssetPrefix}{++temporary:000000}" : $"TEST-{number++:0000}";
            var floor = floors[index % floors.Length];
            var owner = units[index % units.Length];

            var device = new Equipment
            {
                Id = Guid.NewGuid(),
                Type = type,
                AssetNumber = asset,
                AssetNumberKey = TextNormalizer.Key(asset),
                HasTemporaryAssetNumber = noAsset,
                SerialNumber = noAsset ? null : $"SN-{asset}",
                Brand = brands[index % brands.Length],
                Model = $"{prefix}-{100 + index}",
                OwnerUnitId = owner.Id,
                BuildingId = floor.BuildingId,
                FloorId = floor.Id,
                LocationUnitId = index % 5 == 4 ? units[(index + 1) % units.Length].Id : owner.Id,
                Room = $"اتاق {100 + index % 40}",
                Station = index % 3 == 0 ? $"استیشن {1 + index % 6}" : null,
                Status = (index % 10) switch
                {
                    7 => EquipmentStatus.InRepair,
                    8 => EquipmentStatus.InStorage,
                    9 => EquipmentStatus.Retired,
                    _ => EquipmentStatus.Active
                },
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            fill?.Invoke(device);
            device.ComputerNameKey = TextNormalizer.Key(device.ComputerName);
            device.SearchText = EquipmentEndpoints.BuildSearchText(device);

            db.Equipment.Add(device);
            return device;
        }

        void AddParts(Equipment device)
        {
            var position = 0;

            void Part(ComponentKind kind, string? brand, string? model, string? capacity) =>
                db.Components.Add(new Component
                {
                    Id = Guid.NewGuid(), EquipmentId = device.Id, Kind = kind, Brand = brand, Model = model, Capacity = capacity, Position = position++
                });

            Part(ComponentKind.Motherboard, "DemoBoard", $"MB-{random.Next(100, 999)}", null);
            Part(ComponentKind.Cpu, "DemoChip", $"CPU-{Pick("i3", "i5", "i7")}", null);
            Part(ComponentKind.Ram, "DemoMem", null, Pick("4GB", "8GB", "16GB"));
            if (random.Next(2) == 0) Part(ComponentKind.Ram, "DemoMem", null, Pick("4GB", "8GB"));
            Part(random.Next(2) == 0 ? ComponentKind.Ssd : ComponentKind.Hdd, "DemoDisk", null, Pick("256GB", "512GB", "1TB"));
            if (random.Next(3) == 0) Part(ComponentKind.PowerSupply, "DemoPower", "PSU-500", null);
        }

        void FillHub(Equipment device, string name, int index)
        {
            device.ComputerName = $"{name}-{index + 1:000}";
            device.HasMouse = index % 2 == 0;
            device.MouseModel = device.HasMouse ? $"Mouse-{index % 5}" : null;
            device.HasKeyboard = index % 3 != 0;
            device.KeyboardModel = device.HasKeyboard ? $"Keyboard-{index % 4}" : null;
            AddParts(device);
        }

        var cases = new List<Equipment>();
        var laptops = new List<Equipment>();

        for (var i = 0; i < perType; i++)
        {
            var index = i;
            cases.Add(AddDevice(EquipmentType.Case, index, "Tower", ["TestBrand", "DemoTech", "SampleCo"], d => FillHub(d, "TEST-PC", index)));
        }

        for (var i = 0; i < perType; i++)
        {
            var index = i;
            laptops.Add(AddDevice(EquipmentType.Laptop, index, "Book", ["TestBook", "DemoTech", "MockLap"], d => FillHub(d, "TEST-LT", index)));
        }

        // دستگاه متصل همان محل مرکزش را دارد؛ اسقاطی‌ها وصل نمی‌شوند.
        void LinkTo(Equipment child, Equipment hub)
        {
            if (child.Status == EquipmentStatus.Retired || hub.Status == EquipmentStatus.Retired) return;

            child.ParentId = hub.Id;
            child.BuildingId = hub.BuildingId;
            child.FloorId = hub.FloorId;
            child.LocationUnitId = hub.LocationUnitId;
            child.Room = hub.Room;
            child.Station = hub.Station;
            child.SearchText = EquipmentEndpoints.BuildSearchText(child);
        }

        for (var i = 0; i < perType; i++)
        {
            var size = Pick("19", "22", "24", "27", "32");
            var monitor = AddDevice(EquipmentType.Monitor, i, "View", ["TestView", "DemoScreen", "SampleDisplay"], d => d.SizeText = size);
            if (i < 35) LinkTo(monitor, cases[i]);
        }

        for (var i = 0; i < perType; i++)
        {
            var size = Pick("A4", "A3");
            var printer = AddDevice(EquipmentType.Printer, i, "Print", ["TestPrint", "DemoJet", "MockLaser"], d => d.SizeText = size);
            if (i < 15) LinkTo(printer, cases[i + 20]);
        }

        for (var i = 0; i < perType; i++)
        {
            var scanner = AddDevice(EquipmentType.Scanner, i, "Scan", ["TestScan", "DemoScan"], d => d.SizeText = "A4");
            if (i < 10) LinkTo(scanner, laptops[i]);
        }

        for (var i = 0; i < perType; i++)
        {
            var kind = Pick("اسپیکر", "بارکدخوان", "وب‌کم", "هدست", "کارت‌خوان");
            var peripheral = AddDevice(EquipmentType.Peripheral, i, "Extra", ["TestGear", "DemoGear"], d => d.PeripheralKind = kind);
            if (i < 25) LinkTo(peripheral, laptops[i + 10]);
        }

        await db.SaveChangesAsync();

        Console.WriteLine($"Demo data created: {await db.Equipment.CountAsync()} obviously fake devices.");
        Console.WriteLine("Username: demo-admin");
        Console.WriteLine($"Password: {password}");
        return 0;
    }
}
