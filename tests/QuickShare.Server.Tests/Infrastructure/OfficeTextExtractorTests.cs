using System.IO.Compression;
using System.Text;
using QuickShare.Features.Downloads.Preview;

namespace QuickShare.Server.Tests.Infrastructure;

public sealed class OfficeTextExtractorTests
{
    private static MemoryStream BuildZip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var sw = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                sw.Write(content);
            }
        }
        ms.Position = 0;
        return ms;
    }

    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Fact]
    public void Docx_ExtractsParagraphs_AndEscapesHtml()
    {
        var docx = BuildZip((
            "word/document.xml",
            $"<w:document xmlns:w=\"{WordNs}\"><w:body>" +
            "<w:p><w:r><w:t>Hello &amp; world</w:t></w:r></w:p>" +
            "<w:p><w:r><w:t>&lt;script&gt;alert(1)&lt;/script&gt;</w:t></w:r></w:p>" +
            "</w:body></w:document>"));

        var html = OfficeTextExtractor.ExtractHtml(docx, OfficeTextExtractor.Docx);

        Assert.Contains("<p>Hello &amp; world</p>", html);
        Assert.Contains("<p>&lt;script&gt;alert(1)&lt;/script&gt;</p>", html);
    }

    [Fact]
    public void Xlsx_RendersTable_WithSharedStrings()
    {
        var xlsx = BuildZip(
            ("xl/sharedStrings.xml",
             $"<sst xmlns=\"{SpreadsheetNs}\"><si><t>Name</t></si><si><t>Age</t></si></sst>"),
            ("xl/worksheets/sheet1.xml",
             $"<worksheet xmlns=\"{SpreadsheetNs}\"><sheetData>" +
             "<row><c t=\"s\"><v>0</v></c><c t=\"s\"><v>1</v></c></row>" +
             "</sheetData></worksheet>"));

        var html = OfficeTextExtractor.ExtractHtml(xlsx, OfficeTextExtractor.Xlsx);

        Assert.Contains("<table", html);
        Assert.Contains("<td>Name</td>", html);
        Assert.Contains("<td>Age</td>", html);
    }

    [Fact]
    public void Pptx_ExtractsSlideText()
    {
        var pptx = BuildZip((
            "ppt/slides/slide1.xml",
            "<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
            "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">" +
            "<p:sp><p:txBody><a:p><a:r><a:t>Slide title</a:t></a:r></a:p></p:txBody></p:sp>" +
            "</p:sld>"));

        var html = OfficeTextExtractor.ExtractHtml(pptx, OfficeTextExtractor.Pptx);

        Assert.Contains("office-slide", html);
        Assert.Contains("Slide title", html);
    }

    [Fact]
    public void FormatFor_DetectsByExtension()
    {
        Assert.Equal(OfficeTextExtractor.Docx, OfficeTextExtractor.FormatFor(null, "report.docx"));
        Assert.Equal(OfficeTextExtractor.Xlsx, OfficeTextExtractor.FormatFor("application/octet-stream", "sheet.xlsx"));
        Assert.Equal(OfficeTextExtractor.Pptx, OfficeTextExtractor.FormatFor(null, "deck.pptx"));
        Assert.Null(OfficeTextExtractor.FormatFor("application/pdf", "file.pdf"));
    }
}
