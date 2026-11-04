using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Alerts;

public static class AlertEndpoints
{
    // [تصمیم] انباردار هر روز پشتیبان را دانلود می‌کند؛ بعد از یک روز اعلان می‌آید.
    private const int BackupWarnDays = 1;
    private const long DiskWarnBytes = 2L * 1024 * 1024 * 1024;
    private const long DiskDangerBytes = 500L * 1024 * 1024;

    public static void MapAlertEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Alerts, ListAsync).RequireAuthorization(Permissions.StockView);
    }

    private static async Task<IResult> ListAsync(WarehouseDbContext db, AuditContext audit, PermissionService permissions, DatabaseLocation database)
    {
        var alerts = new List<AlertItem>();

        // وضعیت سرور فقط برای کسی که مسئول پشتیبان است.
        if (await permissions.HasAsync(audit.UserId, Permissions.BackupDownload))
        {
            var backup = BackupMarker.Read(database.FilePath);
            if (backup is null)
            {
                alerts.Add(new AlertItem
                {
                    Key = AlertKeys.Backup,
                    Severity = AlertSeverity.Warning,
                    Title = "پشتیبانی ثبت نشده",
                    Detail = "از این نسخه به بعد پشتیبانی دیده نشده است. کار پشتیبان شبانه را بسازید (docs\\deploy.md) یا از صفحه‌ی «پشتیبان» فایل دیتابیس را دانلود کنید."
                });
            }
            else if (backup.Value.AtUtc < DateTime.UtcNow.AddDays(-BackupWarnDays))
            {
                alerts.Add(new AlertItem
                {
                    Key = AlertKeys.Backup,
                    Severity = AlertSeverity.Warning,
                    Title = "پشتیبان قدیمی است",
                    Detail = $"آخرین پشتیبان {JalaliDate.Format(backup.Value.AtUtc)} ({backup.Value.Where}). از صفحه‌ی «پشتیبان» نسخه‌ی امروز را دانلود کنید و کار پشتیبان شبانه را در Task Scheduler بررسی کنید."
                });
            }

            var free = FreeBytes(database.FilePath);
            if (free is { } bytes && bytes < DiskWarnBytes)
            {
                alerts.Add(new AlertItem
                {
                    Key = AlertKeys.DiskSpace,
                    Severity = bytes < DiskDangerBytes ? AlertSeverity.Danger : AlertSeverity.Warning,
                    Title = "فضای دیسک کم است",
                    Detail = $"روی درایو دیتابیس {JalaliDate.ToPersianDigits((bytes / 1024d / 1024 / 1024).ToString("0.0"))} گیگابایت خالی مانده. اگر پر شود، ذخیره‌ی تغییرات شکست می‌خورد."
                });
            }
        }

        // مهم‌ترها اول.
        static int Rank(string severity) => severity switch { AlertSeverity.Danger => 0, AlertSeverity.Warning => 1, _ => 2 };
        return Results.Ok(alerts.OrderBy(a => Rank(a.Severity)).ToList());
    }

    private static long? FreeBytes(string databaseFile)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(databaseFile));
            return string.IsNullOrEmpty(root) ? null : new DriveInfo(root).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
