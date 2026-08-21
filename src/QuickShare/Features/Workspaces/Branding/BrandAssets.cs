using System.Globalization;
using System.Text.RegularExpressions;

namespace QuickShare.Features.Workspaces.Branding;

/// <summary>
/// Validation for everything a workspace can upload or type into its brand (issues #285, #286).
///
/// Uploaded files are sniffed by magic bytes rather than trusted content types, because the
/// browser's declared type is the uploader's claim about their own file. There is no image
/// library in this project, so a logo is not decoded — the size cap and the byte signature are
/// the guard, and the file is only ever rendered inside an <c>img</c> or an <c>@font-face</c>,
/// where a mismatched file simply fails to render rather than executing anything.
///
/// SVG is refused on purpose. It is a document format that can carry script, and an agency logo
/// is not worth turning the recipient page into an XSS surface.
/// </summary>
public static partial class BrandAssets
{
    public const int MaxLogoBytes = 512 * 1024;
    public const int MaxFontBytes = 512 * 1024;
    public const string LogoContentType = "image/png";
    public const string FontContentType = "font/woff2";

    /// <summary>All brand objects live under this prefix, so the orphan sweep can tell them apart.</summary>
    public const string Prefix = "brand/";

    /// <summary>
    /// Hex colour, three or six digits. Anything else is refused rather than coerced: a colour
    /// that silently becomes black is worse than a form error.
    /// </summary>
    [GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
    private static partial Regex HexColor();

    /// <summary>
    /// Font family names reach CSS, so they are restricted to letters, digits, spaces and
    /// hyphens — no quotes, semicolons or braces that could close the declaration and open a
    /// new one.
    /// </summary>
    [GeneratedRegex("^[A-Za-z0-9 \\-]{1,60}$")]
    private static partial Regex FontName();

    public static bool IsValidColor(string? value) =>
        !string.IsNullOrEmpty(value) && HexColor().IsMatch(value);

    public static bool IsValidFontName(string? value) =>
        !string.IsNullOrEmpty(value) && FontName().IsMatch(value);

    /// <summary>
    /// PNG, JPEG or WebP by signature. GIF is left out because an animated logo on a page
    /// somebody is trying to read a contract on is a choice nobody should be offered.
    /// </summary>
    public static bool LooksLikeImage(ReadOnlySpan<byte> head)
    {
        if (head.Length < 12)
            return false;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            return true;

        // JPEG: FF D8 FF
        if (head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF)
            return true;

        // WebP: "RIFF" .... "WEBP"
        if (head[0] == (byte)'R' && head[1] == (byte)'I' && head[2] == (byte)'F' && head[3] == (byte)'F'
            && head[8] == (byte)'W' && head[9] == (byte)'E' && head[10] == (byte)'B' && head[11] == (byte)'P')
            return true;

        return false;
    }

    /// <summary>
    /// woff2 only: <c>wOF2</c>. Older formats are refused because every browser this app
    /// supports reads woff2, and each extra format is another parser to hand a stranger's file.
    /// </summary>
    public static bool LooksLikeWoff2(ReadOnlySpan<byte> head) =>
        head.Length >= 4 && head[0] == (byte)'w' && head[1] == (byte)'O' && head[2] == (byte)'F' && head[3] == (byte)'2';

    /// <summary>
    /// Keys are per-workspace and per-kind, with a random suffix so replacing an asset produces
    /// a new URL. Without that, a client's browser would keep showing the old logo from cache
    /// for as long as the far-future cache header says.
    /// </summary>
    public static string Key(Guid workspaceId, string kind) =>
        $"{Prefix}{workspaceId:N}/{kind}-{Guid.CreateVersion7():N}";

    /// <summary>
    /// Relative luminance contrast between the accent and its text colour. Enforced at 3:1 so a
    /// brand cannot make its own recipient page unreadable — the point is looking like the
    /// agency, not looking like nothing.
    /// </summary>
    public static double ContrastRatio(string accent, string text)
    {
        var a = Luminance(accent);
        var b = Luminance(text);
        var (lighter, darker) = a >= b ? (a, b) : (b, a);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    private static double Channel(int value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.03928 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        var digits = hex.TrimStart('#');

        // Expand the three-digit shorthand so both forms measure the same colour.
        if (digits.Length == 3)
            digits = string.Concat(digits[0], digits[0], digits[1], digits[1], digits[2], digits[2]);

        return (
            int.Parse(digits.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(digits.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            int.Parse(digits.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>WCAG AA for large text and UI components; the accent is used for both.</summary>
    public const double MinContrastRatio = 3.0;
}
