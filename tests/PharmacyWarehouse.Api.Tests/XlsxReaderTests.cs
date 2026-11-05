using System.IO.Compression;
using System.Text;
using PharmacyWarehouse.Api.DataTools;

namespace PharmacyWarehouse.Api.Tests;

public class XlsxReaderTests
{
    private static readonly string SerialOf20280531 = ((int)(new DateTime(2028, 5, 31) - new DateTime(1899, 12, 30)).TotalDays).ToString();

    private const string Styles = """
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
          <numFmts count="2"><numFmt numFmtId="164" formatCode="yyyy\-mm\-dd"/><numFmt numFmtId="165" formatCode="&quot;d&quot;0"/></numFmts>
          <cellXfs count="4"><xf numFmtId="0"/><xf numFmtId="14"/><xf numFmtId="164"/><xf numFmtId="165"/></cellXfs>
        </styleSheet>
        """;

    private static byte[] Build(string sheetsXml, string rels, params (string Path, string Xml)[] sheets)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Add(string path, string xml)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }

            Add("xl/workbook.xml", $"""<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>{sheetsXml}</sheets></workbook>""");
            Add("xl/_rels/workbook.xml.rels", $"""<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">{rels}</Relationships>""");
            Add("xl/styles.xml", Styles);
            foreach (var (path, xml) in sheets) Add(path, xml);
        }

        return ms.ToArray();
    }

    private static string Sheet(string rows) =>
        $"""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{rows}</sheetData></worksheet>""";

    private static string Str(string reference, string text) => $"""<c r="{reference}" t="inlineStr"><is><t>{text}</t></is></c>""";

    [Fact]
    public void ReadAllSheets_ReturnsNamedSheets_InOrder_SkippingHidden()
    {
        var bytes = Build(
            """<sheet name="ضلع آزمایشی" sheetId="1" r:id="rId1"/><sheet name="مخفی" sheetId="2" state="hidden" r:id="rId2"/><sheet name="اعصاب" sheetId="3" r:id="rId3"/>""",
            """<Relationship Id="rId1" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Target="worksheets/sheet3.xml"/>""",
            ("xl/worksheets/sheet1.xml", Sheet($"""<row r="2">{Str("A2", "نام")}</row>""")),
            ("xl/worksheets/sheet2.xml", Sheet("")),
            ("xl/worksheets/sheet3.xml", Sheet($"""<row r="1">{Str("A1", "x")}</row>""")));

        var sheets = XlsxReader.ReadAllSheets(new MemoryStream(bytes));

        Assert.Equal(["ضلع آزمایشی", "اعصاب"], sheets.Select(s => s.Name));
        Assert.Equal(2, sheets[0].Rows.Count); // جای ردیف ۱ خالی حفظ می‌شود
        Assert.Empty(sheets[0].Rows[0]);
        Assert.Equal("نام", sheets[0].Rows[1][0]);
    }

    [Fact]
    public void DateStyledNumericCell_BecomesGregorianText_PlainNumbersAndQuotedFormatsDoNot()
    {
        var row = $"""
            <row r="3">
              <c r="A3" s="1"><v>{SerialOf20280531}</v></c>
              <c r="B3" s="2"><v>{SerialOf20280531}</v></c>
              <c r="C3"><v>30</v></c>
              <c r="D3" s="3"><v>30</v></c>
              <c r="E3" s="1"><v>0.5</v></c>
            </row>
            """;
        var bytes = Build("""<sheet name="S" sheetId="1" r:id="rId1"/>""", """<Relationship Id="rId1" Target="worksheets/sheet1.xml"/>""",
            ("xl/worksheets/sheet1.xml", Sheet(row)));

        var cells = XlsxReader.ReadAllSheets(new MemoryStream(bytes))[0].Rows[2];

        Assert.Equal("31/05/2028", cells[0]);
        Assert.Equal("31/05/2028", cells[1]); // قالب سفارشی yyyy-mm-dd
        Assert.Equal("30", cells[2]);
        Assert.Equal("30", cells[3]); // "d"0 یک نویسه‌ی داخل نقل‌قول است، تاریخ نیست
        Assert.Equal("0.5", cells[4]); // فقط ساعت، تاریخ نیست
    }

    [Fact]
    public void WriterOutput_RoundTripsThroughReader_MultipleSheets()
    {
        var bytes = XlsxWriter.Write(
            new XlsxSheet("یک", ["الف", "ب"], [["1", "x"]]),
            new XlsxSheet("دو", ["الف", "ب"], [["2", "y"], ["3", "z"]]));

        var sheets = XlsxReader.ReadAllSheets(new MemoryStream(bytes));

        Assert.Equal(["یک", "دو"], sheets.Select(s => s.Name));
        Assert.Equal(3, sheets[1].Rows.Count);
        Assert.Equal("y", sheets[1].Rows[1][1]);
        Assert.Equal(sheets[0].Rows.Count, XlsxReader.ReadFirstSheet(new MemoryStream(bytes)).Count);
    }

    [Theory]
    [InlineData("not a zip at all")]
    [InlineData("")]
    public void NotAnXlsx_ThrowsInvalidData(string content) =>
        Assert.Throws<InvalidDataException>(() => XlsxReader.ReadAllSheets(new MemoryStream(Encoding.UTF8.GetBytes(content))));

    [Fact]
    public void MissingRelationship_ThrowsInvalidData_NotKeyNotFound()
    {
        var bytes = Build("""<sheet name="S" sheetId="1" r:id="rId9"/>""", """<Relationship Id="rId1" Target="worksheets/sheet1.xml"/>""",
            ("xl/worksheets/sheet1.xml", Sheet("")));

        Assert.Throws<InvalidDataException>(() => XlsxReader.ReadAllSheets(new MemoryStream(bytes)));
    }
}
