namespace PharmacyWarehouse.Domain.Auth;

public sealed record PermissionInfo(string Key, string Label, string Group);

// فهرست مجوزها در یک جا. هر کلید هم نام policy در Api است و هم در Client برای نمایش دکمه‌ها می‌آید.
public static class Permissions
{
    // نوع claim در جدول‌های Identity.
    public const string RoleClaimType = "perm";
    public const string UserGrantClaimType = "perm.grant";
    public const string UserDenyClaimType = "perm.deny";

    public const string StockView = "stock.view";
    public const string WithdrawalCreate = "withdrawal.create";
    public const string WithdrawalReview = "withdrawal.review";
    public const string StockReceive = "stock.receive";
    public const string StockAdjust = "stock.adjust";
    public const string ProductsManage = "products.manage";
    public const string DashboardView = "dashboard.view";
    public const string ReportsView = "reports.view";
    public const string DataImport = "data.import";
    public const string DataExport = "data.export";
    public const string BackupDownload = "backup.download";
    public const string SettingsManage = "settings.manage";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(StockView, "دیدن اقلام، موجودی و محل‌ها", "انبار"),
        new(WithdrawalCreate, "ثبت برداشت", "انبار"),
        new(StockReceive, "ورود جنس", "انبار"),
        new(StockAdjust, "اصلاح شمارش، اسقاط و انبارگردانی", "انبار"),
        new(ProductsManage, "تعریف قلم، بارکد، محل و حداقل موجودی", "انبار"),
        new(WithdrawalReview, "تأیید و رد برداشت‌ها", "بررسی و گزارش"),
        new(DashboardView, "دیدن داشبورد", "بررسی و گزارش"),
        new(ReportsView, "دیدن گزارش‌ها و تاریخچه", "بررسی و گزارش"),
        new(DataImport, "ورود از Excel", "داده"),
        new(DataExport, "خروجی Excel", "داده"),
        new(BackupDownload, "دانلود پشتیبان", "داده"),
        new(SettingsManage, "تنظیمات (آستانه‌ی انقضا)", "مدیریت"),
        new(UsersManage, "مدیریت کاربران", "مدیریت"),
        new(RolesManage, "مدیریت نقش‌ها و مجوزها", "مدیریت")
    ];

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);
}
