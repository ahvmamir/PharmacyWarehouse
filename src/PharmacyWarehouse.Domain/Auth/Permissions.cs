namespace PharmacyWarehouse.Domain.Auth;

public sealed record PermissionInfo(string Key, string Label, string Group);

// فهرست مجوزها در یک جا. هر کلید هم نام policy در Api است و هم در Client برای نمایش دکمه‌ها می‌آید.
public static class Permissions
{
    // نوع claim در جدول‌های Identity.
    public const string RoleClaimType = "perm";
    public const string UserGrantClaimType = "perm.grant";
    public const string UserDenyClaimType = "perm.deny";

    public const string EquipmentView = "equipment.view";
    public const string EquipmentManage = "equipment.manage";
    public const string EquipmentMove = "equipment.move";
    public const string EquipmentLink = "equipment.link";
    public const string EquipmentChangeStatus = "equipment.status";
    public const string EquipmentRestore = "equipment.restore";
    public const string EquipmentDelete = "equipment.delete";
    public const string EquipmentVerify = "equipment.verify";
    public const string LocationsManage = "locations.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string ReportsView = "reports.view";
    public const string DataExport = "data.export";
    public const string DataImport = "data.import";
    public const string BackupDownload = "backup.download";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(EquipmentView, "دیدن و جست‌وجوی تجهیزات", "تجهیزات"),
        new(EquipmentManage, "ثبت و ویرایش دستگاه", "تجهیزات"),
        new(EquipmentMove, "جابه‌جایی دستگاه", "تجهیزات"),
        new(EquipmentLink, "اتصال و جدا کردن", "تجهیزات"),
        new(EquipmentChangeStatus, "تغییر وضعیت و اسقاط", "تجهیزات"),
        new(EquipmentRestore, "بازگرداندن از اسقاط", "تجهیزات"),
        new(EquipmentDelete, "حذف دستگاه", "تجهیزات"),
        new(EquipmentVerify, "تأیید حضور (انبارگردانی)", "تجهیزات"),
        new(LocationsManage, "مدیریت ساختمان، طبقه و واحد", "مدیریت"),
        new(UsersManage, "مدیریت کاربران", "مدیریت"),
        new(RolesManage, "مدیریت نقش‌ها و مجوزها", "مدیریت"),
        new(ReportsView, "دیدن گزارش‌ها و آخرین تغییرات", "گزارش و داده"),
        new(DataExport, "خروجی Excel", "گزارش و داده"),
        new(DataImport, "ورود لیست از Excel", "گزارش و داده"),
        new(BackupDownload, "دانلود بکاپ", "گزارش و داده")
    ];

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);
}
