using Microsoft.EntityFrameworkCore;
using PharmacyWarehouse.Api.Auth;
using PharmacyWarehouse.Api.Data;
using PharmacyWarehouse.Api.Operations;
using PharmacyWarehouse.Domain.Assets;
using PharmacyWarehouse.Domain.Auth;
using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;
using PharmacyWarehouse.Domain.Time;

namespace PharmacyWarehouse.Api.Alerts;

public static class AlertEndpoints
{
    private const int MaxEntries = 100;
    private const int BackupWarnDays = 2;
    private const int LongRepairDays = 30;
    private const long DiskWarnBytes = 2L * 1024 * 1024 * 1024;
    private const long DiskDangerBytes = 500L * 1024 * 1024;

    public static void MapAlertEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Alerts, ListAsync).RequireAuthorization(Permissions.EquipmentView);
    }

    private static string N(int value) => JalaliDate.ToPersianDigits(value.ToString());

    private static async Task<IResult> ListAsync(WarehouseDbContext db, AuditContext audit, PermissionService permissions, DatabaseLocation database)
    {
        var alerts = new List<AlertItem>();

        // نام کامپیوتر تکراری: دو دستگاه فعال با یک نام در شبکه تداخل دارند. اسقاطی‌ها حساب نمی‌شوند.
        var duplicateKeys = await db.Equipment.AsNoTracking()
            .Where(e => e.ComputerNameKey != null && e.Status != EquipmentStatus.Retired)
            .GroupBy(e => e.ComputerNameKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync();

        if (duplicateKeys.Count > 0)
        {
            var devices = await db.Equipment.AsNoTracking()
                .Where(e => duplicateKeys.Contains(e.ComputerNameKey) && e.Status != EquipmentStatus.Retired)
                .OrderBy(e => e.ComputerNameKey).ThenBy(e => e.AssetNumberKey)
                .Select(e => new { e.Id, e.ComputerName, e.AssetNumber, e.Type, Unit = e.LocationUnit!.Name, e.Room })
                .ToListAsync();

            alerts.Add(new AlertItem
            {
                Key = AlertKeys.DuplicateComputerNames,
                Severity = AlertSeverity.Warning,
                Title = "نام کامپیوتر تکراری",
                Detail = $"{N(duplicateKeys.Count)} نام بین {N(devices.Count)} دستگاه مشترک است. دو کامپیوتر با یک نام در شبکه با هم تداخل دارند؛ یکی را عوض کنید.",
                Count = duplicateKeys.Count,
                Entries = devices.Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{d.ComputerName}: {AuditLabels.Type(d.Type)} {d.AssetNumber}",
                    Sub = string.Join("، ", new[] { d.Unit, d.Room }.Where(v => !string.IsNullOrEmpty(v))),
                    EquipmentId = d.Id
                }).ToList()
            });
        }

        // کیفیت داده: سریال تکراری معمولاً یعنی یک دستگاه دو بار ثبت شده یا سریال اشتباه تایپ شده.
        var serials = await db.Equipment.AsNoTracking()
            .Where(e => e.SerialNumber != null && e.Status != EquipmentStatus.Retired)
            .Select(e => new { e.Id, e.SerialNumber, e.AssetNumber, e.Type, Unit = e.LocationUnit!.Name })
            .ToListAsync();
        // مقایسه‌ی متن در حافظه با کلید یکدست، نه در SQLite.
        var serialGroups = serials.GroupBy(e => TextNormalizer.Key(e.SerialNumber)).Where(g => g.Key is not null && g.Count() > 1).ToList();
        if (serialGroups.Count > 0)
        {
            alerts.Add(new AlertItem
            {
                Key = AlertKeys.DuplicateSerials,
                Severity = AlertSeverity.Warning,
                Title = "سریال تکراری",
                Detail = $"{N(serialGroups.Count)} سریال بین چند دستگاه مشترک است؛ احتمالاً یک دستگاه دو بار ثبت شده یا سریال اشتباه است.",
                Count = serialGroups.Count,
                Entries = serialGroups.SelectMany(g => g).Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{d.SerialNumber}: {AuditLabels.Type(d.Type)} {d.AssetNumber}",
                    Sub = d.Unit,
                    EquipmentId = d.Id
                }).ToList()
            });
        }

        // دستگاه متصل باید همان‌جای کیس یا لپ‌تاپش باشد؛ فرق یعنی یکی جابه‌جا شده و دیگری نه.
        var linked = await db.Equipment.AsNoTracking()
            .Where(e => e.ParentId != null && e.Status != EquipmentStatus.Retired)
            .Select(e => new
            {
                e.Id, e.Type, e.AssetNumber, e.BuildingId, e.FloorId, e.LocationUnitId, e.Room, Unit = e.LocationUnit!.Name,
                ParentType = e.Parent!.Type, ParentAsset = e.Parent.AssetNumber, ParentBuilding = e.Parent.BuildingId,
                ParentFloor = e.Parent.FloorId, ParentUnit = e.Parent.LocationUnitId, ParentRoom = e.Parent.Room,
                ParentUnitName = e.Parent.LocationUnit!.Name
            })
            .ToListAsync();
        var mismatched = linked
            .Where(e => e.BuildingId != e.ParentBuilding || e.FloorId != e.ParentFloor || e.LocationUnitId != e.ParentUnit
                        || TextNormalizer.Key(e.Room) != TextNormalizer.Key(e.ParentRoom))
            .ToList();
        if (mismatched.Count > 0)
        {
            alerts.Add(new AlertItem
            {
                Key = AlertKeys.LinkedPlaceMismatch,
                Severity = AlertSeverity.Warning,
                Title = "محل دستگاه متصل با کیسش یکی نیست",
                Detail = "یکی جابه‌جا شده و دیگری نه. محل درست را در صفحه‌ی دستگاه ثبت کنید.",
                Count = mismatched.Count,
                Entries = mismatched.Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{AuditLabels.Type(d.Type)} {d.AssetNumber} ← {AuditLabels.Type(d.ParentType)} {d.ParentAsset}",
                    Sub = $"{d.Unit}{(string.IsNullOrEmpty(d.Room) ? "" : "، " + d.Room)} / {d.ParentUnitName}{(string.IsNullOrEmpty(d.ParentRoom) ? "" : "، " + d.ParentRoom)}",
                    EquipmentId = d.Id
                }).ToList()
            });
        }

        var bare = await db.Equipment.AsNoTracking()
            .Where(e => (e.Type == EquipmentType.Case || e.Type == EquipmentType.Laptop)
                        && e.Status != EquipmentStatus.Retired && !e.Components.Any())
            .OrderBy(e => e.AssetNumberKey)
            .Select(e => new { e.Id, e.AssetNumber, e.Type, e.Brand, e.Model, Unit = e.LocationUnit!.Name })
            .ToListAsync();
        if (bare.Count > 0)
        {
            alerts.Add(new AlertItem
            {
                Key = AlertKeys.HubWithoutComponents,
                Severity = AlertSeverity.Info,
                Title = "کیس یا لپ‌تاپ بدون قطعه",
                Detail = $"قطعات {N(bare.Count)} دستگاه ثبت نشده؛ «کپی از دستگاه دیگر» در فرم ویرایش کار را کوتاه می‌کند.",
                Count = bare.Count,
                Entries = bare.Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{AuditLabels.Type(d.Type)} {d.AssetNumber} {d.Brand} {d.Model}".TrimEnd(),
                    Sub = d.Unit,
                    EquipmentId = d.Id
                }).ToList()
            });
        }

        var temporary = await db.Equipment.AsNoTracking()
            .Where(e => e.HasTemporaryAssetNumber && e.Status != EquipmentStatus.Retired)
            .OrderBy(e => e.AssetNumberKey)
            .Select(e => new { e.Id, e.AssetNumber, e.Type, e.Brand, e.Model, Unit = e.LocationUnit!.Name })
            .ToListAsync();

        if (temporary.Count > 0)
        {
            alerts.Add(new AlertItem
            {
                Key = AlertKeys.TemporaryAssets,
                Severity = AlertSeverity.Info,
                Title = "شماره اموال موقت",
                Detail = $"{N(temporary.Count)} دستگاه هنوز شماره‌ی موقت دارند؛ شماره‌ی واقعی را از برچسب اموال پیدا و ثبت کنید.",
                Count = temporary.Count,
                Entries = temporary.Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{d.AssetNumber}: {AuditLabels.Type(d.Type)} {d.Brand} {d.Model}".TrimEnd(),
                    Sub = d.Unit,
                    EquipmentId = d.Id
                }).ToList()
            });
        }

        // «در تعمیر» طولانی: از تاریخ ارسال ثبت‌شده در سابقه‌ی تعمیر؛ اگر سابقه‌ای نیست، از آخرین تغییر دستگاه.
        var repairSince = DateTime.UtcNow.AddDays(-LongRepairDays);
        var inRepair = await db.Equipment.AsNoTracking()
            .Where(e => e.Status == EquipmentStatus.InRepair)
            .Select(e => new
            {
                e.Id, e.AssetNumber, e.Type, e.UpdatedAtUtc, Unit = e.OwnerUnit!.Name,
                Sent = db.Repairs.Where(r => r.EquipmentId == e.Id && r.ReturnedAtUtc == null).Max(r => (DateTime?)r.SentAtUtc),
                Vendor = db.Repairs.Where(r => r.EquipmentId == e.Id && r.ReturnedAtUtc == null).Select(r => r.Vendor).FirstOrDefault()
            })
            .ToListAsync();
        var repair = inRepair
            .Select(e => new { e.Id, e.AssetNumber, e.Type, e.Unit, e.Vendor, Since = e.Sent ?? e.UpdatedAtUtc, FromRecord = e.Sent is not null })
            .Where(e => e.Since < repairSince)
            .OrderBy(e => e.Since)
            .ToList();

        if (repair.Count > 0)
        {
            alerts.Add(new AlertItem
            {
                Key = AlertKeys.LongRepair,
                Severity = AlertSeverity.Info,
                Title = $"بیش از {N(LongRepairDays)} روز «در تعمیر»",
                Detail = "اگر دستگاه برگشته، وضعیتش را عوض کنید؛ اگر نه، پیگیری کنید.",
                Count = repair.Count,
                Entries = repair.Take(MaxEntries).Select(d => new AlertEntry
                {
                    Text = $"{AuditLabels.Type(d.Type)} {d.AssetNumber}",
                    Sub = d.FromRecord
                        ? $"{d.Unit}، ارسال {JalaliDate.FormatDate(d.Since)}{(d.Vendor is null ? "" : "، " + d.Vendor)}"
                        : $"{d.Unit}، آخرین تغییر {JalaliDate.FormatDate(d.Since)}",
                    EquipmentId = d.Id
                }).ToList()
            });
        }

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
                    Detail = "از این نسخه به بعد پشتیبانی دیده نشده است. کار پشتیبان شبانه را بسازید (docs\\deploy.md) یا از صفحه‌ی «بکاپ و Excel» فایل دیتابیس را دانلود کنید."
                });
            }
            else if (backup.Value.AtUtc < DateTime.UtcNow.AddDays(-BackupWarnDays))
            {
                alerts.Add(new AlertItem
                {
                    Key = AlertKeys.Backup,
                    Severity = AlertSeverity.Warning,
                    Title = "پشتیبان قدیمی است",
                    Detail = $"آخرین پشتیبان {JalaliDate.Format(backup.Value.AtUtc)} ({backup.Value.Where}). کار پشتیبان شبانه را در Task Scheduler بررسی کنید."
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
