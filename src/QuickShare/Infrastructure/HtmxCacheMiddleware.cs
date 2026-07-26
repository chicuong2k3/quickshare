namespace QuickShare.Infrastructure;

public sealed class HtmxCacheMiddleware
{
    private readonly RequestDelegate _next;

    private static readonly TimeSpan PrivateDefault = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan PublicDefault = TimeSpan.FromMinutes(5);

    private static readonly HashSet<string> NoCachePaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/send", "/login", "/logout", "/upgrade",
        "/account/billing", "/p2p/send"
    };

    private static readonly HashSet<string> PublicPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/", "/pricing"
    };

    public HtmxCacheMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        if (IsCachable(context))
        {
            ApplyCacheHeaders(context);
        }
    }

    private static bool IsCachable(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        if (context.Response.StatusCode is < 200 or >= 300)
        {
            return false;
        }

        if (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (NoCachePaths.Contains(context.Request.Path.Value ?? ""))
        {
            return false;
        }

        return true;
    }

    private static void ApplyCacheHeaders(HttpContext context)
    {
        var isPublic = PublicPaths.Contains(context.Request.Path.Value ?? "");
        var maxAge = isPublic ? PublicDefault : PrivateDefault;
        var visibility = isPublic ? "public" : "private";

        if (context.Response.Headers.ContainsKey("Cache-Control"))
        {
            return;
        }

        context.Response.Headers["Cache-Control"] = $"{visibility}, max-age={(int)maxAge.TotalSeconds}";
        context.Response.Headers["Vary"] = "Cookie, HX-Request";
    }
}
