namespace PharmacyWarehouse.Domain.Contracts;

// یک خطای اعتبارسنجی برای یک فیلد؛ سرور مرجع است و پاسخ ۴۰۰ همین‌ها را برمی‌گرداند.
public sealed record ValidationError(string Field, string Message);
