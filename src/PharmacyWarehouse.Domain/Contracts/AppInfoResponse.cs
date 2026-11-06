namespace PharmacyWarehouse.Domain.Contracts;

// نام برنامه، نام سازمان از تنظیمات سرور (Branding:OrganizationName) و نسخه؛ بدون ورود هم خواندنی،
// چون صفحه‌ی ورود هم آن را نشان می‌دهد. نام بیمارستان در کد و Git نیست.
public sealed class AppInfoResponse
{
    public const string ProductName = "داروبار";

    public string Product { get; init; } = ProductName;
    public string? Organization { get; init; }
    public string? Version { get; init; }
}
