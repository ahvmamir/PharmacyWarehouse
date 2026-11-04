namespace PharmacyWarehouse.Domain.Contracts;

public static class ApiHeaders
{
    // هدر ضد CSRF: کلاینت ما روی هر درخواست غیر GET این را می‌فرستد.
    public const string RequestedWith = "X-Requested-With";
    public const string RequestedWithValue = "PharmacyWarehouse";
}