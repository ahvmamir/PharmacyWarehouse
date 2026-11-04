namespace PharmacyWarehouse.Domain.Stock;

// قواعد قلم و بچ. فعلاً فقط سقف طول‌ها؛ Validate را Task 3 با قرارداد SaveProductRequest اضافه می‌کند.
public static class ProductRules
{
    public const int MaxName = 200;
    public const int MaxBrand = 100;
    public const int MaxPack = 100;
    public const int MaxBatch = 50;
    public const int MaxCode = 100;
    public const int MaxNote = 500;
}
