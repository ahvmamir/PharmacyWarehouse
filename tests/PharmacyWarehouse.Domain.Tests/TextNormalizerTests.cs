using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Tests;

public class TextNormalizerTests
{
    [Theory]
    [InlineData("۱۲۳۴۵۶۷", "1234567")]
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("ab۱1٣x", "ab113x")]
    [InlineData("1234567", "1234567")]
    [InlineData("", "")]
    public void ToAsciiDigits_ConvertsPersianAndArabicDigits(string input, string expected)
    {
        Assert.Equal(expected, TextNormalizer.ToAsciiDigits(input));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("‌ ‌", null)]
    [InlineData("  abc  ", "abc")]
    [InlineData("a   b\t c", "a b c")]
    [InlineData("كيس", "کیس")] // Arabic kaf and yeh become Persian
    [InlineData("۱۲", "12")]
    [InlineData("‌abc‌", "abc")]
    [InlineData("a‌b", "a‌b")] // a joiner inside a word stays in the display form
    public void Clean_NormalizesForDisplay(string? input, string? expected)
    {
        Assert.Equal(expected, TextNormalizer.Clean(input));
    }

    [Theory]
    [InlineData("۱۲۳۴", "1234")]
    [InlineData(" 1234 ", "1234")]
    [InlineData("12 34", "1234")]
    [InlineData("test-0001", "TEST-0001")]
    [InlineData("a‌b", "AB")]
    [InlineData(null, null)]
    public void Key_RemovesAllSpacingAndUppercases(string? input, string? expected)
    {
        Assert.Equal(expected, TextNormalizer.Key(input));
    }

    [Theory]
    [InlineData("كيس  abc", "کیس ABC")]
    [InlineData("a‌b c", "AB C")]
    [InlineData("   ", null)]
    public void SearchKey_KeepsOneSpaceBetweenWords(string input, string? expected)
    {
        Assert.Equal(expected, TextNormalizer.SearchKey(input));
    }
}
