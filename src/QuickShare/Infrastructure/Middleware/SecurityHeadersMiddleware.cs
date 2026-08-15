using Microsoft.Extensions.Options;

namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// Security headers for every response (issue #82). Cheap trust signal that
/// appears in scanner reports (Mozilla Observatory, securityheaders.com).
/// CSP allows 'unsafe-inline' scripts because this app is Blazor SSR + htmx
/// with inline per-page config blocks (ZAPDROP_CONFIG, ad init, theme init)
/// and inline scripts inside htmx partials — nonce-hashing all of them is a
/// larger refactor; the policy still blocks scripts from foreign origins,
/// frames, object/embed, and base-uri tampering.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeaderOptions _options;

    private const string HstsValue = "max-age=31536000; includeSubDomains";
    private const string NoSniff = "nosniff";
    private const string ReferrerStrictOriginWhenCrossOrigin = "strict-origin-when-cross-origin";
    private const string PermissionsDefault =
        "camera=(), microphone=(), geolocation=(), payment=(), usb=(), magnetometer=(), gyroscope=()";

    public SecurityHeadersMiddleware(RequestDelegate next, IOptions<SecurityHeaderOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var (ctx, options) = ((HttpContext, SecurityHeaderOptions))state;
            ApplyHeaders(ctx, options);
            return Task.CompletedTask;
        }, (context, _options));

        await _next(context);
    }

    private static void ApplyHeaders(HttpContext context, SecurityHeaderOptions options)
    {
        if (!options.Enabled)
            return;

        if (options.HstsEnabled && context.Request.IsHttps)
            context.Response.Headers[HttpHeaders.StrictTransportSecurity] = HstsValue;

        context.Response.Headers[HttpHeaders.XContentTypeOptions] = NoSniff;
        context.Response.Headers[HttpHeaders.ReferrerPolicy] = ReferrerStrictOriginWhenCrossOrigin;
        context.Response.Headers[HttpHeaders.PermissionsPolicy] = PermissionsDefault;

        if (!string.IsNullOrWhiteSpace(options.ContentSecurityPolicy))
            context.Response.Headers[HttpHeaders.ContentSecurityPolicy] = options.ContentSecurityPolicy;
    }
}

/// <summary>Security-header policy (issue #82). Config-driven so CSP can be tuned without a deploy.</summary>
public sealed class SecurityHeaderOptions
{
    public const string Section = "SecurityHeaders";

    public bool Enabled { get; set; } = true;
    public bool HstsEnabled { get; set; } = true;

    /// <summary>
    /// Full CSP string. 'unsafe-inline' is required for script because the
    /// Blazor SSR shell + htmx partials ship inline config blocks; everything
    /// else is locked down (self-only default, frame-ancestors none, no object/embed).
    /// connect-src allows https: because R2 presigned URLs live on a different origin.
    /// </summary>
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://daub.dev; " +
        "style-src 'self' 'unsafe-inline' https://daub.dev https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com data:; " +
        "img-src 'self' data: blob: https:; " +
        "connect-src 'self' https: wss: ws:; " +
        "frame-ancestors 'none'; " +
        "base-uri 'self'; " +
        "form-action 'self'; " +
        "object-src 'none'";
}
