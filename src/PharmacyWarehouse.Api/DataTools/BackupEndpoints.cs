using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;

namespace PharmacyWarehouse.Api.DataTools;

// دانلود پشتیبان کامل. ورود و خروج Excel در برش ۴ (الگو: DataEndpoints در Emu).
public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.DataBackup, BackupAsync).RequireAuthorization(Permissions.BackupDownload);
    }

    private static string Stamp() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");

    // نسخه‌ی سازگار دیتابیس، با تاریخچه و کاربران. فقط با مجوز مخصوص، چون hash رمزها در آن است.
    private static async Task<IResult> BackupAsync(WarehouseDbContext db, DatabaseLocation database)
    {
        var target = Path.Combine(Path.GetTempPath(), $"pharmacywarehouse-backup-{Guid.NewGuid():N}.db");
        try
        {
            await OperationCommands.VacuumIntoAsync(db, target);
            var bytes = await File.ReadAllBytesAsync(target);
            BackupMarker.Write(database.FilePath, "دانلود از برنامه");
            return Results.File(bytes, "application/octet-stream", $"pharmacywarehouse-{Stamp()}.db");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(target)) File.Delete(target);
        }
    }
}
