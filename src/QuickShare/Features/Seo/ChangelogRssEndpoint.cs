using System.Globalization;
using System.Text;

namespace QuickShare.Features.Seo;

/// <summary>
/// GET /changelog.xml — RSS 2.0 feed for the product changelog (issue #207).
/// </summary>
public sealed class ChangelogRssEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(RoutePaths.ChangelogRss);
        AllowAnonymous();
        Description(b => b.ExcludeFromDescription());
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var req = HttpContext.Request;
        var baseUrl = $"{req.Scheme}://{req.Host}";
        var changelogUrl = $"{baseUrl}{RoutePaths.Changelog}";

        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<rss version=\"2.0\">");
        sb.AppendLine("  <channel>");
        sb.AppendLine("    <title>QuickShareCloud Changelog</title>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"    <link>{changelogUrl}</link>");
        sb.AppendLine("    <description>QuickShareCloud release changelog and updates.</description>");
        sb.AppendLine("    <language>en</language>");
        sb.AppendLine("    <item>");
        sb.AppendLine("      <title>2026-08-16 — Link protection &amp; management</title>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      <link>{changelogUrl}#2026-08-16</link>");
        sb.AppendLine("      <guid isPermaLink=\"false\">quickshare-changelog-2026-08-16</guid>");
        sb.AppendLine("      <pubDate>Sun, 16 Aug 2026 00:00:00 GMT</pubDate>");
        sb.AppendLine("      <description>Revoke a link before expiry; rename files after upload; share password strength meter; clone link; burn-after-download; SHA-256 checksum; custom alias; favorites; daily download cap.</description>");
        sb.AppendLine("    </item>");
        sb.AppendLine("    <item>");
        sb.AppendLine("      <title>2026-08-15 — Platform security</title>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      <link>{changelogUrl}#2026-08-15</link>");
        sb.AppendLine("      <guid isPermaLink=\"false\">quickshare-changelog-2026-08-15</guid>");
        sb.AppendLine("      <pubDate>Sat, 15 Aug 2026 00:00:00 GMT</pubDate>");
        sb.AppendLine("      <description>Rate limiting, account lockout, new-device alerts, download CAPTCHA, abuse + DMCA reporting, security headers + CSP, 7-day Pro trial.</description>");
        sb.AppendLine("    </item>");
        sb.AppendLine("    <item>");
        sb.AppendLine("      <title>2026-08-14 — Growth &amp; mobile</title>");
        sb.AppendLine(CultureInfo.InvariantCulture, $"      <link>{changelogUrl}#2026-08-14</link>");
        sb.AppendLine("      <guid isPermaLink=\"false\">quickshare-changelog-2026-08-14</guid>");
        sb.AppendLine("      <pubDate>Fri, 14 Aug 2026 00:00:00 GMT</pubDate>");
        sb.AppendLine("      <description>OpenGraph cards, SEO pages, file previews (image/video/PDF), PWA + Android Share Target, native share sheet + QR.</description>");
        sb.AppendLine("    </item>");
        sb.AppendLine("  </channel>");
        sb.AppendLine("</rss>");

        HttpContext.Response.Headers.CacheControl = "public, max-age=300";
        await Send.StringAsync(sb.ToString(), StatusCodes.Status200OK, "application/rss+xml; charset=utf-8", ct);
    }
}
