namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// Cache strategy: allowlist. Only anonymous, content-stable pages are cacheable;
/// everything else (account pages, upgrade, download, API responses) is no-store so
/// a user never sees stale personal data after a mutation. Responses that already
/// set their own Cache-Control (e.g. MapStaticAssets fingerprinted assets) win.
/// </summary>
public sealed class HtmxCacheMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly TimeSpan PublicMaxAge = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        RoutePaths.Home, RoutePaths.Pricing
    };

    private const string CacheControlHeader = "Cache-Control";
    private const string VaryHeader = "Vary";
    private const string NoStore = "no-store";
    private const string VaryValue = "Cookie, HX-Request";

    public HtmxCacheMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var ctx = (HttpContext)state;
            ApplyCacheHeaders(ctx);
            return Task.CompletedTask;
        }, context);

        await _next(context);
    }

    private static void ApplyCacheHeaders(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return;
        }

        if (context.Response.StatusCode is < 200 or >= 300)
        {
            return;
        }

        if (PublicPaths.Contains(context.Request.Path.Value ?? string.Empty))
        {
            // Override: Blazor SSR stamps "no-store, no-cache" on rendered pages,
            // so the public allowlist must win over an already-set header.
            context.Response.Headers[CacheControlHeader] = $"public, max-age={(int)PublicMaxAge.TotalSeconds}";
            context.Response.Headers[VaryHeader] = VaryValue;
            return;
        }

        // Anything that already declared its own policy (MapStaticAssets immutable
        // assets, Blazor's no-store) keeps it; otherwise default to no-store.
        if (!context.Response.Headers.ContainsKey(CacheControlHeader))
        {
            context.Response.Headers[CacheControlHeader] = NoStore;
        }
    }
}
