using System.IO.Compression;
using System.Net;
using System.Text;
using System.Xml.Linq;

namespace QuickShare.Features.Downloads.Preview;

/// <summary>
/// Extracts plain-text content from Office Open XML documents (docx/xlsx/pptx) as escaped
/// HTML (issue #94). Office files are ZIP archives; each format's XML is parsed with LINQ
/// to XML and every text node is HTML-escaped, so a hostile document can never inject markup.
/// </summary>
public static class OfficeTextExtractor
{
    public const string Docx = "docx";
    public const string Xlsx = "xlsx";
    public const string Pptx = "pptx";

    public static string? FormatFor(string? contentType, string? fileName)
    {
        if (contentType?.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase) == true
            || fileName?.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) == true)
            return Docx;

        if (contentType?.Contains("spreadsheetml", StringComparison.OrdinalIgnoreCase) == true
            || fileName?.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) == true)
            return Xlsx;

        if (contentType?.Contains("presentationml", StringComparison.OrdinalIgnoreCase) == true
            || fileName?.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase) == true)
            return Pptx;

        return null;
    }

    public static string ExtractHtml(Stream stream, string format)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        return format switch
        {
            Docx => ExtractDocx(zip),
            Xlsx => ExtractXlsx(zip),
            Pptx => ExtractPptx(zip),
            _ => string.Empty,
        };
    }

    private static string ExtractDocx(ZipArchive zip)
    {
        var entry = zip.GetEntry("word/document.xml");
        if (entry is null) return string.Empty;

        var doc = XDocument.Load(entry.Open());
        var paragraphs = doc.Descendants().Where(e => e.Name.LocalName == "p");
        var sb = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            var text = string.Concat(
                paragraph.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
            if (string.IsNullOrWhiteSpace(text)) continue;
            sb.Append("<p>").Append(Escape(text)).Append("</p>");
        }
        return sb.ToString();
    }

    private static string ExtractXlsx(ZipArchive zip)
    {
        var sharedStrings = ReadSharedStrings(zip);

        var entry = zip.GetEntry("xl/worksheets/sheet1.xml");
        if (entry is null) return string.Empty;

        var sheet = XDocument.Load(entry.Open());
        var rows = sheet.Descendants().Where(e => e.Name.LocalName == "row");
        var sb = new StringBuilder("<table class=\"office-table\">");
        foreach (var row in rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var type = cell.Attribute("t")?.Value;
                var raw = cell.Descendants().Where(e => e.Name.LocalName == "v").Select(v => v.Value).FirstOrDefault() ?? string.Empty;
                var display = type == "s" && int.TryParse(raw, out var idx) && idx >= 0 && idx < sharedStrings.Count
                    ? sharedStrings[idx]
                    : raw;
                sb.Append("<td>").Append(Escape(display)).Append("</td>");
            }
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var result = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return result;

        var doc = XDocument.Load(entry.Open());
        foreach (var si in doc.Descendants().Where(e => e.Name.LocalName == "si"))
        {
            var text = string.Concat(
                si.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
            result.Add(text);
        }
        return result;
    }

    private static string ExtractPptx(ZipArchive zip)
    {
        var slides = zip.Entries
            .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase)
                && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        foreach (var slide in slides)
        {
            var doc = XDocument.Load(slide.Open());
            var texts = doc.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value).ToList();
            if (texts.Count == 0) continue;
            sb.Append("<div class=\"office-slide\">");
            foreach (var text in texts)
                sb.Append("<p>").Append(Escape(text)).Append("</p>");
            sb.Append("</div>");
        }
        return sb.ToString();
    }

    private static string Escape(string text) => WebUtility.HtmlEncode(text);
}
