namespace PharmacyWarehouse.Domain.Contracts;

public static class ApiRoutes
{
    public const string Health = "/api/health";

    public const string Login = "/api/auth/login";
    public const string Logout = "/api/auth/logout";
    public const string Me = "/api/auth/me";
    public const string ChangePassword = "/api/auth/change-password";
    public const string SetInitialPassword = "/api/auth/set-initial-password";

    public const string Buildings = "/api/buildings";
    public const string Floors = "/api/floors";
    public const string Units = "/api/units";
    public const string Equipment = "/api/equipment";
    public const string EquipmentBulk = "/api/equipment/bulk";
    public const string EquipmentBulkMove = "/api/equipment/bulk-move";
    public const string EquipmentBulkStatus = "/api/equipment/bulk-status";
    public const string EquipmentSummary = "/api/equipment/summary";
    public const string EquipmentLookup = "/api/equipment/lookup";
    public const string Locations = "/api/locations";
    public const string Users = "/api/users";
    public const string Roles = "/api/roles";
    public const string Reports = "/api/reports";
    public const string DataTemplate = "/api/data/template";
    public const string DataExport = "/api/data/export";
    public const string DataBackup = "/api/data/backup";
    public const string DataImportPreview = "/api/data/import/preview";
    public const string DataImportCommit = "/api/data/import/commit";
    public const string DataImportCheck = "/api/data/import/check";
    public const string DataImportCommitRows = "/api/data/import/commit-rows";
    public const string RecentChangeSets = "/api/changes";
    public const string Alerts = "/api/alerts";
    public const string AppInfo = "/api/app-info";

    // مسیرهای ساخته‌شده برای کلاینت
    public static string ById(string collection, Guid id) => $"{collection}/{id}";
    public static string Deactivate(string collection, Guid id) => $"{collection}/{id}/deactivate";
    public static string Activate(string collection, Guid id) => $"{collection}/{id}/activate";
    public static string Dependencies(string collection, Guid id) => $"{collection}/{id}/dependencies";
    public static string Report(string key, string query) => $"{Reports}/{key}?{query}";
    public static string ReportExcel(string key, string query) => $"{Reports}/{key}/excel?{query}";
    public static string UserResetPassword(Guid id) => $"{Users}/{id}/reset-password";
    public static string EquipmentMove(Guid id) => $"{Equipment}/{id}/move";
    public static string EquipmentStatus(Guid id) => $"{Equipment}/{id}/status";
    public static string EquipmentLink(Guid id) => $"{Equipment}/{id}/link";
    public static string EquipmentUnlink(Guid id) => $"{Equipment}/{id}/unlink";
    public static string EquipmentHistory(Guid id) => $"{Equipment}/{id}/history";
    public static string EquipmentVerify(Guid id) => $"{Equipment}/{id}/verify";
    public static string EquipmentRepairs(Guid id) => $"{Equipment}/{id}/repairs";
    public static string RepairReturn(Guid repairId) => $"/api/repairs/{repairId}/return";
}
