using System.Net;

namespace QuickShare.Features.Downloads.Shared;

/// <summary>
/// The screenshot-marking overlay for in-page previews (issue #155).
///
/// A tiled SVG, not a canvas redraw and not a server-side re-encode: the stored file is never
/// touched, so the download stays byte-identical to what was uploaded and the zero-knowledge
/// story holds. What is being defended is the *preview* — the agency's fear is a client
/// screenshotting work-in-progress and forwarding it, and a name across the picture removes the
/// anonymity that makes that comfortable.
///
/// ponytail: CSS background tile. It is defeatable by anyone determined — an overlay always is.
/// Burning pixels into a transcoded preview is the upgrade path, and it costs a media pipeline.
/// </summary>
public static class PreviewWatermark
{
    public const int MaxTextLength = 60;
    private const int TileWidth = 260;
    private const int TileHeight = 150;

    /// <summary>
    /// What to write across the preview: the workspace's own line if it has one, otherwise the
    /// sender's name. Falls back to nothing — a watermark reading "Preview" marks nobody and
    /// would only degrade the picture.
    /// </summary>
    public static string? TextFor(string? brandHeader, string? senderName)
    {
        var text = Trim(brandHeader) ?? Trim(senderName);
        return text is null || text.Length <= MaxTextLength ? text : text[..MaxTextLength];
    }

    /// <summary>
    /// The tile as a CSS <c>url()</c> value, ready for a custom property.
    ///
    /// The text is XML-escaped and then percent-encoded, so a name containing a quote or an
    /// angle bracket cannot close the element and start a new one inside the data URI.
    /// </summary>
    public static string ToCssUrl(string text)
    {
        var safe = WebUtility.HtmlEncode(text);
        var svg =
            $"<svg xmlns='http://www.w3.org/2000/svg' width='{TileWidth}' height='{TileHeight}'>" +
            $"<text x='20' y='100' transform='rotate(-30 {TileWidth / 2} {TileHeight / 2})' " +
            "font-family='system-ui,sans-serif' font-size='18' fill='rgba(255,255,255,0.42)'>" +
            $"{safe}</text></svg>";

        return $"url(\"data:image/svg+xml,{Uri.EscapeDataString(svg)}\")";
    }

    private static string? Trim(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
