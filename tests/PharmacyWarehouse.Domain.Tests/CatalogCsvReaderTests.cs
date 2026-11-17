using System.Text;
using PharmacyWarehouse.Domain.Import;

namespace PharmacyWarehouse.Domain.Tests;

// خواننده‌ی CSV فهرست مرجع (بخش ۱۵.۴ spec)، فقط با داده‌ی ساختگی. GTINهای آزمایشی رقم کنترل درست دارند.
public class CatalogCsvReaderTests
{
    private const string Header = "Irc,GTIN,GenericCode,DrugGenericName,BrandName,PersianBrandName,LicenseOwnerCompanyName,BrandOwnerCompanyName,ProducerCompanyName,ProducerCountryName,PackageCount,ConsumerPrice,IsTemporaryIRC";

    private static CatalogReadResult ReadBytes(string text, bool bom)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bom) bytes = [0xEF, 0xBB, 0xBF, .. bytes];
        return CatalogCsvReader.Read(new MemoryStream(bytes));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReadsRow_WithOrWithoutBom(bool bom)
    {
        var csv = Header + "\r\nTEST-0001,00000000000017,G1,Test Generic,\"TEST BRAND, 10 MG\",داروی آزمایشی,شرکت آزمایشی,شرکت آزمایشی,تولیدکننده‌ی آزمایشی,کشور آزمایشی,30,12345,TRUE\r\n";

        var result = ReadBytes(csv, bom);

        Assert.Null(result.MissingColumn);
        var row = Assert.Single(result.Rows);
        Assert.Equal("00000000000017", row.Gtin);
        Assert.Equal("TEST BRAND, 10 MG", row.BrandName);
        Assert.Equal("داروی آزمایشی", row.PersianBrandName);
        Assert.Equal("تولیدکننده‌ی آزمایشی", row.Producer);
        Assert.Equal(30, row.PackageCount);
        Assert.Equal(12345, row.ConsumerPrice);
        Assert.True(row.IsTemporaryIrc);
    }

    [Fact]
    public void ColumnsByName_NotOrder_AndCaseInsensitive()
    {
        var csv = "brandname,gtin,IRC\nTEST BRAND,00000000000024,TEST-0002\n";

        var row = Assert.Single(CatalogCsvReader.Read(csv).Rows);

        Assert.Equal("TEST-0002", row.Irc);
        Assert.Equal("00000000000024", row.Gtin);
        Assert.Null(row.PersianBrandName);
        Assert.Null(row.PackageCount);
    }

    [Fact]
    public void QuotedField_WithEscapedQuoteAndNewline()
    {
        var csv = "Irc,GTIN,BrandName\nTEST-0003,00000000000031,\"TEST \"\"Q\"\"\nLINE\"\n";

        Assert.Equal("TEST \"Q\" LINE", Assert.Single(CatalogCsvReader.Read(csv).Rows).BrandName);
    }

    [Fact]
    public void EmptyRows_AreSkippedAndCounted()
    {
        var csv = "Irc,GTIN,BrandName\nTEST-0001,00000000000017,TEST\n,,\n , , \n";

        var result = CatalogCsvReader.Read(csv);

        Assert.Single(result.Rows);
        Assert.Equal(2, result.Empty);
    }

    [Fact]
    public void InvalidGtin_IsRejected_ShortGtinIsPadded()
    {
        var csv = "Irc,GTIN,BrandName\nTEST-0001,00000000000018,BAD CHECK\nTEST-0002,,NO GTIN\nTEST-0003,0000000000031,THIRTEEN\nTEST-0004,ABC,LETTERS\n";

        var result = CatalogCsvReader.Read(csv);

        Assert.Equal(3, result.Invalid);
        Assert.Equal("00000000000031", Assert.Single(result.Rows).Gtin);
    }

    [Fact]
    public void DuplicateGtin_IsKept_AndCounted()
    {
        var csv = "Irc,GTIN,BrandName\nTEST-0001,00000000000048,A\nTEST-0002,00000000000048,B\n";

        var result = CatalogCsvReader.Read(csv);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(1, result.DuplicateGtin);
    }

    [Theory]
    [InlineData("GTIN,BrandName\n", "Irc")]
    [InlineData("Irc,BrandName\n", "GTIN")]
    [InlineData("Irc,GTIN\n", "BrandName")]
    [InlineData("", "Irc")]
    public void MissingRequiredColumn_IsReported(string csv, string column)
    {
        var result = CatalogCsvReader.Read(csv);

        Assert.Equal(column, result.MissingColumn);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Text_IsNormalized()
    {
        var csv = "Irc,GTIN,BrandName,PersianBrandName\nTEST-0001,۰۰۰۰۰۰۰۰۰۰۰۰۱۷,  TEST   X ,داروي آزمايشي\n";

        var row = Assert.Single(CatalogCsvReader.Read(csv).Rows);

        Assert.Equal("00000000000017", row.Gtin);
        Assert.Equal("TEST X", row.BrandName);
        Assert.Equal("داروی آزمایشی", row.PersianBrandName);
    }

    [Fact]
    public void Mapping_PrefersPersianName_AndFormatsPack()
    {
        Assert.Equal("داروی آزمایشی", CatalogMapping.Name("داروی آزمایشی", "TEST"));
        Assert.Equal("TEST", CatalogMapping.Name(null, "TEST"));
        Assert.Equal("30 عدد", CatalogMapping.Pack(30));
        Assert.Null(CatalogMapping.Pack(null));
    }
}
