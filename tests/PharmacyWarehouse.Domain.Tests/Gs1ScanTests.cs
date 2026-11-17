using PharmacyWarehouse.Domain.Text;

namespace PharmacyWarehouse.Domain.Tests;

// همه‌ی کدها ساختگی‌اند (GTIN با رقم کنترل درست، UID و بچ آشکارا آزمایشی)؛ ساختار از جعبه‌های واقعی گرفته شده، نه مقدارها.
public class Gs1ScanTests
{
    private const char Gs = Gs1Scan.Gs;

    // GTIN ساختگی با رقم کنترل حساب‌شده.
    private static string Gtin(string first13)
    {
        var sum = 0;
        for (var i = 0; i < 13; i++) sum += (first13[12 - i] - '0') * (i % 2 == 0 ? 3 : 1);
        return first13 + (10 - sum % 10) % 10;
    }

    private static readonly string TestGtin = Gtin("0626000000001");
    private const string TestUid = "99990000000000000001";

    [Fact]
    public void WithGsSeparators_ReadsAllFields()
    {
        var data = Gs1Scan.Parse($"01{TestGtin}21{TestUid}{Gs}17280331{Gs}10TESTB01");

        Assert.NotNull(data);
        Assert.Equal(TestGtin, data.Gtin);
        Assert.Equal(TestUid, data.Serial);
        Assert.Equal("TESTB01", data.Batch);
        Assert.Equal(new DateOnly(2028, 3, 31), data.Expiry);
        Assert.Equal("31/03/2028", data.ExpiryText);
    }

    [Fact]
    public void WithoutSeparators_PrefersTwentyDigitUid()
    {
        var data = Gs1Scan.Parse($"01{TestGtin}21{TestUid}1728033110TESTB01");

        Assert.NotNull(data);
        Assert.Equal(TestUid, data.Serial);
        Assert.Equal("TESTB01", data.Batch);
        Assert.Equal(new DateOnly(2028, 3, 31), data.Expiry);
    }

    [Fact]
    public void WithoutSeparators_ReturnsAllCandidates_ForBatchMatching()
    {
        var all = Gs1Scan.ParseAll($"01{TestGtin}21{TestUid}1728033110TESTB01");

        Assert.Contains(all, c => c.Batch == "TESTB01" && c.Serial == TestUid);
        Assert.All(all, c => Assert.Equal(TestGtin, c.Gtin));
    }

    [Fact]
    public void Parenthesized_IsRead()
    {
        var data = Gs1Scan.Parse($"(01){TestGtin}(17)290514(10)TEST26188(21){TestUid}");

        Assert.NotNull(data);
        Assert.Equal("TEST26188", data.Batch);
        Assert.Equal(new DateOnly(2029, 5, 14), data.Expiry);
    }

    [Fact]
    public void DayZero_MeansEndOfMonth_AndMonthOnlyText()
    {
        var data = Gs1Scan.Parse($"01{TestGtin}17300200{Gs}10TESTB02");

        Assert.NotNull(data);
        Assert.Equal(new DateOnly(2030, 2, 28), data.Expiry);
        Assert.True(data.ExpiryMonthOnly);
        Assert.Equal("02/2030", data.ExpiryText);
    }

    [Fact]
    public void SymbologyPrefix_AndPersianDigits_AreAccepted()
    {
        var persian = string.Concat($"01{TestGtin}17280331").Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c);

        var data = Gs1Scan.Parse("]d2" + string.Concat(persian) + Gs + "10TESTB03");

        Assert.NotNull(data);
        Assert.Equal(TestGtin, data.Gtin);
        Assert.Equal("TESTB03", data.Batch);
    }

    [Theory]
    [InlineData("6260000000017")]          // EAN-13 معمولی
    [InlineData("TEST-0001")]
    [InlineData("01123")]                  // کوتاه
    [InlineData("0106260000000010")]       // رقم کنترل غلط
    [InlineData("")]
    public void NonGs1_ReturnsNull(string raw) => Assert.Null(Gs1Scan.Parse(raw));

    [Fact]
    public void InvalidExpiryMonth_IsRejected() =>
        Assert.Null(Gs1Scan.Parse($"01{TestGtin}17281331{Gs}10TESTB04"));

    [Fact]
    public void CodeVariants_LinksGtin14AndEan13()
    {
        Assert.Equal([TestGtin, TestGtin[1..]], Gs1Scan.CodeVariants(TestGtin));
        Assert.Equal([TestGtin[1..], TestGtin], Gs1Scan.CodeVariants(TestGtin[1..]));
        Assert.Equal(["TEST-0001"], Gs1Scan.CodeVariants("TEST-0001"));
    }
}
