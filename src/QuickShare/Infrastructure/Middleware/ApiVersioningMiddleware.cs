namespace QuickShare.Infrastructure.Middleware;

/// <summary>
/// API versioning strategy and contract guarantees (issue #199).
///
/// Rewrites versioned paths (<c>/api/v1/...</c> and <c>/api/v2/...</c>) to the canonical
/// <c>/api/...</c> endpoint tree so existing and v1-pinned SDK clients share one handler set,
/// while stamping <c>X-API-Version</c> on every API response and RFC 8594 <c>Sunset</c> +
/// <c>Deprecation</c> + <c>Link</c> headers when a deprecated version or endpoint is called.
/// </summary>
public sealed class ApiVersioningMiddleware(RequestDelegate next)
{
    public const string CurrentVersion = "1.0";
    public const string VersionHeader = "X-API-Version";
    public const string DeprecationHeader = "Deprecation";
    public const string SunsetHeader = "Sunset";
    public const string SunsetDateRfc1123 = "Wed, 10 Nov 2027 00:00:00 GMT";
    public const string MigrationGuideLink = "</api-docs#migration>; rel=\"deprecation\"";

    private const string V1Prefix = "/api/v1/";
    private const string V2Prefix = "/api/v2/";
    private const string ApiPrefix = "/api/";

    public async Task InvokeAsync(HttpContext context)
    {
        var rawPath = context.Request.Path.Value ?? string.Empty;
        string? resolvedVersion = null;
        var isDeprecated = false;

        if (rawPath.StartsWith(V1Prefix, StringComparison.OrdinalIgnoreCase))
        {
            resolvedVersion = "1.0";
            context.Request.Path = "/api/" + rawPath[V1Prefix.Length..];
        }
        else if (rawPath.StartsWith(V2Prefix, StringComparison.OrdinalIgnoreCase))
        {
            resolvedVersion = "2.0";
            context.Request.Path = "/api/" + rawPath[V2Prefix.Length..];
        }
        else if (rawPath.StartsWith(ApiPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var requestedHeader = context.Request.Headers[VersionHeader].FirstOrDefault();
            resolvedVersion = string.IsNullOrWhiteSpace(requestedHeader) ? CurrentVersion : requestedHeader.Trim();
            if (string.Equals(resolvedVersion, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedVersion, "v0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(context.Request.Headers["X-API-Deprecated"].FirstOrDefault(), "1", StringComparison.Ordinal))
            {
                isDeprecated = true;
            }
        }

        if (resolvedVersion is not null)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers[VersionHeader] = resolvedVersion;
                if (isDeprecated)
                {
                    context.Response.Headers[DeprecationHeader] = "true";
                    context.Response.Headers[SunsetHeader] = SunsetDateRfc1123;
                    context.Response.Headers.Link = MigrationGuideLink;
                }
                return Task.CompletedTask;
            });
        }

        await next(context);
    }
}
