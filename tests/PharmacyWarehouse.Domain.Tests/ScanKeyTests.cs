using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Tests;

public class ScanKeyTests
{
    [Theory]
    [InlineData("AB12", "AB12")]
    [InlineData("شذ12", "AB12")] // صفحه‌کلید فارسی استاندارد ویندوز: ش=A، ذ=B
    [InlineData("*TEST123*", "TEST123")]
    [InlineData(" test-123 ", "TEST-123")]
    [InlineData("۱۲۳٤٥", "12345")] // ارقام فارسی و عربی
    [InlineData("ضصثق", "QWER")]
    [InlineData("ي", "D")]
    [InlineData("ك", ";")]
    [InlineData("a b\tc", "ABC")]
    public void ScanKey_Normalizes(string input, string expected) =>
        Assert.Equal(expected, TextNormalizer.ScanKey(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  * ")]
    public void ScanKey_EmptyIsNull(string? input) => Assert.Null(TextNormalizer.ScanKey(input));
}
