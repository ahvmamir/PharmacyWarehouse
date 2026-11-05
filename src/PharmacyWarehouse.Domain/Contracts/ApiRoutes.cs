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
    public const string Products = "/api/products";
    public const string Receipts = "/api/receipts";
    public const string Withdrawals = "/api/withdrawals";
    public const string Users = "/api/users";
    public const string Roles = "/api/roles";
    public const string DataBackup = "/api/data/backup";
    public const string DataExport = "/api/data/export";
    public const string ImportParse = "/api/import/parse";
    public const string ImportValidate = "/api/import/validate";
    public const string ImportCommit = "/api/import/commit";
    public const string ImportTemplate = "/api/import/template";
    public const string Alerts = "/api/alerts";
    public const string AppInfo = "/api/app-info";
    public const string Dashboard = "/api/dashboard";
    public const string Settings = "/api/settings";

    // مسیرهای ساخته‌شده برای کلاینت
    public static string ById(string collection, Guid id) => $"{collection}/{id}";
    public static string Deactivate(string collection, Guid id) => $"{collection}/{id}/deactivate";
    public static string Activate(string collection, Guid id) => $"{collection}/{id}/activate";
    public static string Dependencies(string collection, Guid id) => $"{collection}/{id}/dependencies";
    public const string Batches = "/api/batches";
    public const string Stocktakes = "/api/stocktakes";
    public const string ReportMovements = "/api/reports/movements";
    public const string ReportConsumption = "/api/reports/consumption";
    public const string LabelProducts = "/api/labels/products";
    public const string LabelProductsGenerate = "/api/labels/products/generate";
    public static string BatchAdjust(Guid id) => $"{Batches}/{id}/adjust";
    public static string BatchDispose(Guid id) => $"{Batches}/{id}/dispose";
    public static string StocktakeBatches(Guid id, Guid productId) => $"{Stocktakes}/{id}/batches?productId={productId}";
    public static string StocktakeCounts(Guid id) => $"{Stocktakes}/{id}/counts";
    public static string StocktakeClose(Guid id) => $"{Stocktakes}/{id}/close";
    public static string StocktakeApprove(Guid id, Guid countId) => $"{Stocktakes}/{id}/counts/{countId}/approve";
    public const string ReceiptsBulk = "/api/receipts/bulk";
    public static string ProductSearch(string query) => $"{Products}/search?q={Uri.EscapeDataString(query)}";
    public static string ProductByCode(string code) => $"{Products}/by-code/{Uri.EscapeDataString(code)}";
    public static string ProductBarcodes(Guid id) => $"{Products}/{id}/barcodes";
    public static string ProductBarcode(Guid id, Guid barcodeId) => $"{Products}/{id}/barcodes/{barcodeId}";
    public const string WithdrawalReviewList = "/api/withdrawals/review";
    public const string WithdrawalBulkApprove = "/api/withdrawals/approve";
    public static string WithdrawalApprove(Guid id) => $"{Withdrawals}/{id}/approve";
    public static string WithdrawalReject(Guid id) => $"{Withdrawals}/{id}/reject";
    public static string ProductAllocation(Guid id, int quantity, int minutesAgo = 0) => $"{Products}/{id}/allocation?quantity={quantity}&minutesAgo={minutesAgo}";
    public static string ProductHistory(Guid id) => $"{Products}/{id}/history";
    public static string UserResetPassword(Guid id) => $"{Users}/{id}/reset-password";
}
