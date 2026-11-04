namespace PharmacyWarehouse.Domain.Auth;

// نقش‌های پیش‌فرض (بخش ۱۱ spec). نام‌ها در کد فقط پیش‌فرض‌اند؛ مجوز هر نقش از صفحه‌ی نقش‌ها تغییر می‌کند.
// فقط Admin در کد معنی ویژه دارد: همیشه همه‌ی مجوزها را دارد و قفل است.
public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Keeper = "Keeper";
    public const string Manager = "Manager";

    public static readonly IReadOnlyList<string> Defaults = [Admin, Staff, Keeper, Manager];

    public static string Label(string? role) => role switch
    {
        Admin => "مدیر سیستم",
        Staff => "کارکنان",
        Keeper => "انباردار",
        Manager => "مدیر داروخانه",
        null or "" => "بدون نقش",
        _ => role
    };
}
