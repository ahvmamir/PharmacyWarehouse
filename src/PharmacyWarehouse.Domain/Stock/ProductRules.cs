using PharmacyWarehouse.Domain.Contracts;
using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Stock;

// قواعد قلم و بچ؛ سرور مرجع است.
public static class ProductRules
{
    public const int MaxName = 200;
    public const int MaxBrand = 100;
    public const int MaxPack = 100;
    public const int MaxBatch = 50;
    public const int MaxCode = 100;
    public const int MaxNote = 500;

    // وجود و فعال بودن LocationId را سرور (با دیتابیس) می‌سنجد.
    public static List<ValidationError> Validate(SaveProductRequest request)
    {
        var errors = new List<ValidationError>();
        var name = TextNormalizer.Clean(request.Name);
        if (name is null) errors.Add(new("Name", "نام قلم را وارد کنید."));
        else if (name.Length > MaxName) errors.Add(new("Name", $"نام قلم حداکثر {MaxName} نویسه است."));

        if (TextNormalizer.Clean(request.Brand) is { Length: > MaxBrand }) errors.Add(new("Brand", $"برند حداکثر {MaxBrand} نویسه است."));
        if (TextNormalizer.Clean(request.PackDescription) is { Length: > MaxPack }) errors.Add(new("PackDescription", $"بسته‌بندی حداکثر {MaxPack} نویسه است."));
        if (request.MinStock is < 0) errors.Add(new("MinStock", "حداقل موجودی نمی‌تواند منفی باشد."));
        return errors;
    }
}
