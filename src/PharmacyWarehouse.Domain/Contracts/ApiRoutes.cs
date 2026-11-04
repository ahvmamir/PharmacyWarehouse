namespace PharmacyWarehouse.Domain.Contracts;

public static class ApiRoutes
{
    public const string Health = "/api/health";

    public const string Login = "/api/auth/login";
    public const string Logout = "/api/auth/logout";
    public const string Me = "/api/auth/me";
    public const string ChangePassword = "/api/auth/change-password";
    public const string SetInitialPassword = "/api/auth/set-initial-password";

    public const string Locations = "/api/locations";
    public const string Users = "/api/users";
    public const string Roles = "/api/roles";
    public const string DataBackup = "/api/data/backup";
    public const string Alerts = "/api/alerts";
    public const string AppInfo = "/api/app-info";

    // مسیرهای ساخته‌شده برای کلاینت
    public static string ById(string collection, Guid id) => $"{collection}/{id}";
    public static string Deactivate(string collection, Guid id) => $"{collection}/{id}/deactivate";
    public static string Activate(string collection, Guid id) => $"{collection}/{id}/activate";
    public static string Dependencies(string collection, Guid id) => $"{collection}/{id}/dependencies";
    public static string UserResetPassword(Guid id) => $"{Users}/{id}/reset-password";
}
