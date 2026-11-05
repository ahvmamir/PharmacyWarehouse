using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Users;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Api.Operations;

// فرمان‌های نگهداری: migrate و backup برای سرور.
public static class OperationCommands
{
    public static async Task<int> MigrateAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WarehouseDbContext>().Database.MigrateAsync();

        Console.WriteLine("Database is up to date.");
        return 0;
    }

    // کد خروج ۱ یعنی ناسازگاری؛ برای استفاده در اسکریپت و Task Scheduler.
    public static async Task<int> VerifyStockAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var (count, mismatches) = await StockVerifier.VerifyAsync(scope.ServiceProvider.GetRequiredService<WarehouseDbContext>());

        foreach (var m in mismatches)
        {
            Console.WriteLine($"MISMATCH batch {m.BatchId} ({m.BatchNumber}): OnHand {m.OnHand}, movements {m.MovementSum}");
        }

        Console.WriteLine($"Checked {count} batches, {mismatches.Count} mismatched. Nothing was changed.");
        return mismatches.Count == 0 ? 0 : 1;
    }

    // فقط در Development؛ روی دیتابیس واقعی هرگز داده‌ی ساختگی نمی‌نویسد.
    public static async Task<int> SeedTestDataAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            Console.Error.WriteLine("seed-test-data runs only in the Development environment.");
            return 1;
        }

        using var scope = app.Services.CreateScope();
        var result = await TestDataSeeder.SeedAsync(
            scope.ServiceProvider.GetRequiredService<WarehouseDbContext>(),
            scope.ServiceProvider.GetRequiredService<PharmacyWarehouse.Api.Stock.StockLedger>(),
            DateTime.UtcNow);

        if (result is null)
        {
            Console.Error.WriteLine("Test products already exist; nothing was changed.");
            return 1;
        }

        Console.WriteLine($"Seeded {result.Products} test products and {result.Batches} batches.");
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

        var target = Path.Combine(directory, $"pharmacywarehouse-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

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
        foreach (var old in Directory.GetFiles(directory, "pharmacywarehouse-*.db").OrderDescending().Skip(keep))
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
}
