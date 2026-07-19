namespace QuickShare.Web.Infrastructure;

/// <summary>
/// Redirects unauthenticated visitors of Blazor SSR pages to /login.
/// FastEndpoints APIs return 401 via UseAuthorization — this middleware
/// only handles page-level navigation that falls through authorization.
/// </summary>
public class AuthPageRedirectMiddleware
{
    private static readonly string[] PublicPathPrefixes =
    [
        "/login",
        "/pricing",
        "/t/",
        "/antiforgery",
        "/logout"
    ];

    private readonly RequestDelegate _next;

    public AuthPageRedirectMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Already authenticated → pass through
        if (context.User.Identity?.IsAuthenticated == true)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "";

        // Check allowlist: prefix match
        foreach (var prefix in PublicPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }
        }

        // Not authenticated and not public → redirect to login
        var returnUrl = context.Request.PathBase + context.Request.Path;
        context.Response.Redirect($"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
    }
}
