using System.Security.Claims;

namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// Emits <c>X-Robots-Tag: noindex, nofollow</c> on private delivery responses
/// (issue #121). Delivery links are share-by-link private pages; a leaked link
/// must not become a permanent search-engine result. Marketing pages are
/// deliberately untouched — SEO landing pages stay indexable.
/// </summary>
public sealed class RobotsNoIndexMiddleware
{
    private readonly RequestDelegate _next;

    private const string DeliveryPagePrefix = "/d/";
    private const string ApiDownloadsPrefix = "/api/downloads/";

    public RobotsNoIndexMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsPrivateDeliveryPath(context.Request.Path))
            context.Response.Headers[HttpHeaders.XRobotsTag] = RobotsValues.NoIndexNoFollow;

        await _next(context);
    }

    private static bool IsPrivateDeliveryPath(PathString path) =>
        (path.Value?.StartsWith(DeliveryPagePrefix, StringComparison.OrdinalIgnoreCase) ?? false)
        || (path.Value?.StartsWith(ApiDownloadsPrefix, StringComparison.OrdinalIgnoreCase) ?? false);
}
