using System.Text;

namespace QuickShare.Features.Seo;

/// <summary>
/// XML sitemap (issue #88): lists only PUBLIC static pages — never /d/{code}
/// deliveries, account, admin, or API surfaces. Complements the #28 landing
/// pages and the #121 noindex on recipient pages.
/// </summary>
public sealed class SitemapEndpoint : EndpointWithoutRequest
{
    private static readonly string[] PublicRoutes =
    [
        RoutePaths.Home,
        RoutePaths.Send,
        RoutePaths.Pricing,
        RoutePaths.SeoWeTransferAlternatives,
        RoutePaths.SeoSendLargeFilesFree,
        RoutePaths.SeoP2pFileTransfer,
        RoutePaths.SeoViGuiFileLonMienPhi,
        RoutePaths.Changelog,
        RoutePaths.Roadmap,
        RoutePaths.Compare,
        RoutePaths.WhyPrivate,
        RoutePaths.Terms,
        RoutePaths.Privacy,
        RoutePaths.Security,
        RoutePaths.Abuse,
        RoutePaths.ToolsOnlineClipboard,
        RoutePaths.ToolsSendImagesUncompressed,
        RoutePaths.ToolsShareText,
    ];

    public override void Configure()
    {
        Get(RoutePaths.Sitemap);
        AllowAnonymous();
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        var lastmod = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var route in PublicRoutes)
        {
            xml.Append("<url><loc>");
            xml.Append(baseUrl);
            xml.Append(route);
            xml.Append("</loc><lastmod>");
            xml.Append(lastmod);
            xml.Append("</lastmod></url>");
        }
        xml.Append("</urlset>");

        await Send.StringAsync(xml.ToString(), StatusCodes.Status200OK, "application/xml; charset=utf-8", ct);
    }
}
