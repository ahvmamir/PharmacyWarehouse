using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PharmacyWarehouse.Api.DataTools;

public sealed record XlsxSheetData(string Name, List<string?[]> Rows);

public sealed record XlsxSheet(string Name, IReadOnlyList<string> Columns, IEnumerable<string?[]> Rows, ISet<int>? NumericColumns = null);

// xlsx یک ZIP از چند فایل XML است. همین‌قدر که لازم داریم را خودمان می‌نویسیم و می‌خوانیم،
// تا برای یک جدول ساده بسته‌ی خارجی و وابستگی به دسترسی NuGet نداشته باشیم.
public static class XlsxWriter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private const string ContentTypesHead =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>";

    private const string RootRels =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    // سبک ۰ عادی، سبک ۱ سرستون پررنگ با پس‌زمینه.
    private const string Styles =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
        "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Tahoma\"/></font><font><b/><sz val=\"11\"/><name val=\"Tahoma\"/></font></fonts>" +
        "<fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE3F3F1\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
        "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/></cellXfs>" +
        "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
        "</styleSheet>";

    public static byte[] Write(params XlsxSheet[] sheets)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var types = new StringBuilder(ContentTypesHead);
            for (var i = 1; i <= sheets.Length; i++)
            {
                types.Append($"<Override PartName=\"/xl/worksheets/sheet{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
            }
            types.Append("</Types>");

            Add(zip, "[Content_Types].xml", types.ToString());
            Add(zip, "_rels/.rels", RootRels);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/workbook.xml", BuildWorkbook(sheets));
            Add(zip, "xl/_rels/workbook.xml.rels", BuildWorkbookRels(sheets.Length));

            for (var i = 0; i < sheets.Length; i++)
            {
                WriteSheet(zip, $"xl/worksheets/sheet{i + 1}.xml", sheets[i]);
            }
        }

        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        stream.Write(new UTF8Encoding(false).GetBytes(content));
    }

    private static string BuildWorkbook(XlsxSheet[] sheets)
    {
        var xml = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>");

        for (var i = 0; i < sheets.Length; i++)
        {
            xml.Append($"<sheet name=\"{SecurityEscape(SheetName(sheets[i].Name))}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        }

        return xml.Append("</sheets></workbook>").ToString();
    }

    private static string BuildWorkbookRels(int count)
    {
        var xml = new StringBuilder(
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");

        for (var i = 1; i <= count; i++)
        {
            xml.Append($"<Relationship Id=\"rId{i}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i}.xml\"/>");
        }

        xml.Append($"<Relationship Id=\"rId{count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        return xml.Append("</Relationships>").ToString();
    }

    private static void WriteSheet(ZipArchive zip, string path, XlsxSheet sheet)
    {
        using var stream = zip.CreateEntry(path, CompressionLevel.Fastest).Open();
        using var xml = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });

        xml.WriteStartDocument(true);
        xml.WriteStartElement("worksheet", Main.NamespaceName);

        // برگه راست‌به‌چپ و سرستون ثابت.
        xml.WriteStartElement("sheetViews", Main.NamespaceName);
        xml.WriteStartElement("sheetView", Main.NamespaceName);
        xml.WriteAttributeString("rightToLeft", "1");
        xml.WriteAttributeString("workbookViewId", "0");
        xml.WriteStartElement("pane", Main.NamespaceName);
        xml.WriteAttributeString("ySplit", "1");
        xml.WriteAttributeString("topLeftCell", "A2");
        xml.WriteAttributeString("activePane", "bottomLeft");
        xml.WriteAttributeString("state", "frozen");
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteStartElement("cols", Main.NamespaceName);
        xml.WriteStartElement("col", Main.NamespaceName);
        xml.WriteAttributeString("min", "1");
        xml.WriteAttributeString("max", Math.Max(sheet.Columns.Count, 1).ToString());
        xml.WriteAttributeString("width", "20");
        xml.WriteAttributeString("customWidth", "1");
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteStartElement("sheetData", Main.NamespaceName);

        WriteRow(xml, 1, sheet.Columns.ToArray<string?>(), style: 1, null);

        var number = 2;
        foreach (var row in sheet.Rows)
        {
            WriteRow(xml, number++, row, style: 0, sheet.NumericColumns);
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    private static void WriteRow(XmlWriter xml, int number, string?[] cells, int style, ISet<int>? numeric)
    {
        xml.WriteStartElement("row", Main.NamespaceName);
        xml.WriteAttributeString("r", number.ToString());

        for (var i = 0; i < cells.Length; i++)
        {
            var value = cells[i];
            if (string.IsNullOrEmpty(value)) continue;

            xml.WriteStartElement("c", Main.NamespaceName);
            xml.WriteAttributeString("r", ColumnName(i) + number);
            if (style != 0) xml.WriteAttributeString("s", style.ToString());

            if (numeric?.Contains(i) == true)
            {
                xml.WriteElementString("v", Main.NamespaceName, value);
            }
            else
            {
                // همه‌چیز متن است؛ پس شماره‌ی اموال 00123 صفرش را از دست نمی‌دهد و «=...» هرگز فرمول نمی‌شود.
                xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is", Main.NamespaceName);
                xml.WriteStartElement("t", Main.NamespaceName);
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteString(StripInvalid(value));
                xml.WriteEndElement();
                xml.WriteEndElement();
            }

            xml.WriteEndElement();
        }

        xml.WriteEndElement();
    }

    private static string StripInvalid(string value) =>
        new(value.Where(XmlConvert.IsXmlChar).ToArray());

    private static string SecurityEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? "";

    private static string SheetName(string name)
    {
        var cleaned = new string(name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    public static string ColumnName(int index)
    {
        var name = "";
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + (n - 1) % 26) + name;
        }

        return name;
    }
}

public static class XlsxReader
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    // سقف باز شدن هر بخش فایل، تا فایل فشرده‌ی دستکاری‌شده حافظه را پر نکند.
    private const long MaxEntryBytes = 200 * 1024 * 1024;

    // ردیف‌های برگه‌ی اول، با حفظ جای ستون‌ها. فایل نامعتبر InvalidDataException می‌دهد.
    public static List<string?[]> ReadFirstSheet(Stream stream)
    {
        var sheets = ReadAllSheets(stream);
        return sheets.Count > 0 ? sheets[0].Rows : throw new InvalidDataException("Worksheet not found.");
    }

    // همه‌ی شیت‌های دیده‌شونده با نامشان (ترتیب فایل). سلول تاریخ (عدد سریال با سبک تاریخ) به «dd/MM/yyyy» میلادی تبدیل می‌شود.
    public static List<XlsxSheetData> ReadAllSheets(Stream stream)
    {
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            var shared = zip.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
                ? Load(sharedEntry).Root!.Elements(Main + "si")
                    .Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value)))
                    .ToList()
                : [];
            var dateStyles = DateStyles(zip);

            var workbook = Load(zip.GetEntry("xl/workbook.xml") ?? throw new InvalidDataException("Workbook not found."));
            var rels = Load(zip.GetEntry("xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Relations not found."));
            var targets = rels.Descendants(PackageRel + "Relationship")
                .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value);

            var result = new List<XlsxSheetData>();
            foreach (var sheet in workbook.Descendants(Main + "sheet"))
            {
                if (sheet.Attribute("state")?.Value is "hidden" or "veryHidden") continue;

                var target = targets[sheet.Attribute(Rel + "id")!.Value];
                var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                var entry = zip.GetEntry(path) ?? throw new InvalidDataException("Worksheet not found.");
                result.Add(new XlsxSheetData(sheet.Attribute("name")?.Value ?? "", ReadRows(Load(entry), shared, dateStyles)));
            }

            return result;
        }
        // فایل غیر ZIP یا دستکاری‌شده بسته به محتوا هر کدام از این‌ها را می‌دهد؛ برای کاربر همه «فایل نامعتبر» است.
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or NullReferenceException or ArgumentException or KeyNotFoundException)
        {
            throw new InvalidDataException("Not a readable xlsx file.", ex);
        }
    }

    private static List<string?[]> ReadRows(XDocument sheet, List<string> shared, HashSet<int> dateStyles)
    {
        var rows = new List<string?[]>();
        foreach (var row in sheet.Descendants(Main + "row"))
        {
            var cells = new List<string?>();
            var position = 0;

            foreach (var cell in row.Elements(Main + "c"))
            {
                var reference = cell.Attribute("r")?.Value;
                if (reference is not null) position = ColumnIndex(reference);

                while (cells.Count < position) cells.Add(null);
                cells.Add(ReadCell(cell, shared, dateStyles));
                position++;
            }

            // جای ردیف را هم نگه می‌داریم تا شماره‌ی سطر در خطاها با Excel یکی باشد.
            var rowNumber = int.TryParse(row.Attribute("r")?.Value, out var parsed) ? parsed : rows.Count + 1;
            while (rows.Count < rowNumber - 1) rows.Add([]);
            rows.Add(cells.ToArray());
        }

        return rows;
    }

    // اندیس سبک‌هایی (cellXfs) که قالب تاریخ دارند: قالب‌های داخلی تاریخ یا قالب سفارشی با y/d/m بیرون از نقل‌قول.
    private static HashSet<int> DateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        if (zip.GetEntry("xl/styles.xml") is not { } entry) return result;

        var styles = Load(entry).Root!;
        var custom = styles.Element(Main + "numFmts")?.Elements(Main + "numFmt")
            .Where(n => n.Attribute("numFmtId") is not null && n.Attribute("formatCode") is not null)
            .ToDictionary(n => int.Parse(n.Attribute("numFmtId")!.Value), n => n.Attribute("formatCode")!.Value) ?? [];

        var index = 0;
        foreach (var xf in styles.Element(Main + "cellXfs")?.Elements(Main + "xf") ?? [])
        {
            var id = int.TryParse(xf.Attribute("numFmtId")?.Value, out var parsed) ? parsed : 0;
            if (IsBuiltInDate(id) || (custom.TryGetValue(id, out var code) && IsDateFormat(code))) result.Add(index);
            index++;
        }

        return result;
    }

    private static bool IsBuiltInDate(int id) => id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58;

    private static bool IsDateFormat(string code)
    {
        var inQuotes = false;
        foreach (var c in code)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && char.ToLowerInvariant(c) is 'y' or 'd') return true;
        }

        return false;
    }

    private static XDocument Load(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes) throw new InvalidDataException("File is too large.");

        using var stream = entry.Open();

        // بدون DTD: فایل آپلودی نباید بتواند موجودیت خارجی بارگذاری کند.
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string? ReadCell(XElement cell, List<string> shared, HashSet<int> dateStyles)
    {
        var type = cell.Attribute("t")?.Value;

        if (type == "inlineStr") return string.Concat(cell.Descendants(Main + "t").Select(t => t.Value));

        var value = cell.Element(Main + "v")?.Value;
        if (value is null) return null;

        // عدد سریال تاریخ Excel (مبدأ ۱۸۹۹-۱۲-۳۰) ← میلادی روزانه. سلول فقط‌ساعت (کمتر از ۱) تاریخ نیست.
        if ((type is null or "n") && int.TryParse(cell.Attribute("s")?.Value, out var style) && dateStyles.Contains(style)
            && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var serial)
            && serial >= 1 && serial < 2958466)
        {
            var date = new DateTime(1899, 12, 30).AddDays(Math.Floor(serial));
            return date.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        return type == "s" && int.TryParse(value, out var index) && index >= 0 && index < shared.Count
            ? shared[index]
            : value;
    }

    private static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference.TakeWhile(char.IsLetter))
        {
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return index - 1;
    }
}
