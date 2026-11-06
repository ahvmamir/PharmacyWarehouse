namespace PharmacyWarehouse.Domain.Auth;

public sealed record PermissionInfo(string Key, string Label, string Group, string Description = "");

// فهرست مجوزها در یک جا. هر کلید هم نام policy در Api است و هم در Client برای نمایش دکمه‌ها می‌آید.
public static class Permissions
{
    // نوع claim در جدول‌های Identity.
    public const string RoleClaimType = "perm";
    public const string UserGrantClaimType = "perm.grant";
    public const string UserDenyClaimType = "perm.deny";

    public const string StockView = "stock.view";
    public const string WithdrawalCreate = "withdrawal.create";
    public const string ReceiptSubmit = "receipt.submit";
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

    // policy ترکیبی «یکی از چند مجوز»؛ مجوز نیست و به نقش داده نمی‌شود.
    public const string ProductLookup = "product.lookup";
    public static readonly IReadOnlyList<string> ProductLookupAnyOf = [StockView, WithdrawalCreate, ReceiptSubmit];

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(ReceiptSubmit, "ثبت ورود جنس ساده (بدون تکمیل)", "کار روزانه",
            "در صفحه‌ی ورود و خروج، ورود چندردیفه با نام، بچ و تعداد ثبت می‌کند. موجودی همان لحظه زیاد می‌شود و انباردار بعداً تکمیل و تأیید می‌کند."),
        new(StockView, "دیدن اقلام، موجودی و محل‌ها", "انبار",
            "صفحه‌های اقلام، جزئیات قلم، بچ‌ها، محل‌ها و حرکت‌ها. بدون آن، جست‌وجوی قلم برای ثبت برداشت یا ورود همچنان کار می‌کند."),
        new(WithdrawalCreate, "ثبت برداشت", "انبار",
            "ثبت برداشت از قلم (FEFO) و دیدن برداشت‌های خود."),
        new(StockReceive, "ورود جنس", "انبار",
            "صفحه‌ی ورود جنس کامل و بررسی ورودهای ثبت‌شده توسط پرسنل."),
        new(StockAdjust, "اصلاح شمارش، اسقاط و انبارگردانی", "انبار",
            "انبارگردانی، اصلاح موجودی بچ و اسقاط با ثبت حرکت."),
        new(ProductsManage, "تعریف قلم، بارکد، محل و حداقل موجودی", "انبار",
            "ساخت و ویرایش قلم، افزودن بارکد، ساخت و جابه‌جایی محل‌ها، تنظیم حداقل موجودی و چاپ برچسب."),
        new(WithdrawalReview, "تأیید و رد برداشت‌ها", "بررسی و گزارش",
            "صفحه‌ی بررسی برداشت‌ها: تأیید یا رد (با برگشت موجودی)."),
        new(DashboardView, "دیدن داشبورد", "بررسی و گزارش",
            "داشبورد خلاصه‌ی وضعیت و هشدارهای انقضا و کمبود."),
        new(ReportsView, "دیدن گزارش‌ها و تاریخچه", "بررسی و گزارش",
            "صفحه‌ی گزارش‌ها و تاریخچه‌ی تغییرات."),
        new(DataImport, "ورود از Excel", "داده",
            "بارگذاری فایل Excel، پیش‌نمایش و ثبت اقلام و ورودها."),
        new(DataExport, "خروجی Excel", "داده",
            "گرفتن خروجی Excel از اقلام و گزارش‌ها."),
        new(BackupDownload, "دانلود پشتیبان", "داده",
            "گرفتن و دانلود نسخه‌ی پشتیبان پایگاه داده."),
        new(SettingsManage, "تنظیمات (آستانه‌ی انقضا)", "مدیریت",
            "تغییر تنظیمات برنامه، مثل آستانه‌های هشدار انقضا."),
        new(UsersManage, "مدیریت کاربران", "مدیریت",
            "ساخت، ویرایش، غیرفعال‌سازی و ریست رمز کاربران، و اجازه یا منع مجوز برای هر کاربر."),
        new(RolesManage, "مدیریت نقش‌ها و مجوزها", "مدیریت",
            "ساخت، ویرایش و حذف نقش‌ها و تعیین مجوزهای هر نقش.")
    ];

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);
}
