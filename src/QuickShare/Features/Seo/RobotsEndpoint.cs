namespace QuickShare.Features.Seo;

/// <summary>
/// robots.txt (issue #88): allow public pages, disallow account/admin/API and
/// recipient delivery pages (/d/, /p2p/d/ — already noindexed via #121).
/// </summary>
public sealed class RobotsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(RoutePaths.Robots);
        AllowAnonymous();
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";

        var content = string.Join('\n',
            "User-agent: *",
            "Disallow: /account",
            "Disallow: /admin",
            "Disallow: /api",
            "Disallow: /d/",
            "Disallow: /p2p/d/",
            "Disallow: /hangfire",
            $"Sitemap: {baseUrl}{RoutePaths.Sitemap}",
            string.Empty);

        await Send.StringAsync(content, StatusCodes.Status200OK, "text/plain; charset=utf-8", ct);
    }
}
