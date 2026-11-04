using System.Text;
using PharmacyWarehouse.Domain.Labels;
using Xunit;

namespace PharmacyWarehouse.Domain.Tests;

public class Code128Tests
{
    // میله=1، فاصله=0؛ برای مقایسه با خروجی مرجع.
    private static string Modules(string text)
    {
        var widths = Code128.Widths(text)!;
        var builder = new StringBuilder();
        for (var i = 0; i < widths.Count; i++)
        {
            builder.Append(i % 2 == 0 ? '1' : '0', widths[i]);
        }
        return builder.ToString();
    }

    [Fact]
    public void PatternTable_HasStandardWidths()
    {
        Assert.Equal(107, Code128.Patterns.Length);
        Assert.All(Code128.Patterns.Take(106), p => Assert.Equal(11, p.Sum(c => c - '0')));
        Assert.Equal(13, Code128.Patterns[106].Sum(c => c - '0'));
    }

    // مقدارهای مرجع از کتابخانه‌ی python-barcode.
    [Theory]
    [InlineData("A", "1101001000010100011000100010110001100011101011")]
    [InlineData("12345678", "1101001110010110011100100010110001110001011011000010100100011101101100011101011")]
    public void Encode_MatchesReference(string text, string expected)
    {
        Assert.Equal(expected, Modules(text));
    }

    [Fact]
    public void EvenDigits_UseCodeC_OtherwiseCodeB()
    {
        Assert.Equal(105, Code128.Encode("1234")![0]);
        Assert.Equal(104, Code128.Encode("123")![0]);
        Assert.Equal(104, Code128.Encode("TEMP-000001")![0]);
    }

    [Fact]
    public void PersianDigits_AreConvertedToLatin()
    {
        Assert.Equal(Code128.Encode("1234"), Code128.Encode("۱۲۳۴"));
        Assert.Equal(Code128.Encode("1234"), Code128.Encode("١٢٣٤"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("اموال-۱۲")]
    public void NotEncodable_ReturnsNull(string? text)
    {
        Assert.Null(Code128.Encode(text));
        Assert.Null(Code128.Widths(text));
    }
}
