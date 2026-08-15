namespace QuickShare.Infrastructure.Constants;

public static class HttpHeaders
{
    public const string Range = "Range";
    public const string XRobotsTag = "X-Robots-Tag";
    public const string StrictTransportSecurity = "Strict-Transport-Security";
    public const string XContentTypeOptions = "X-Content-Type-Options";
    public const string ReferrerPolicy = "Referrer-Policy";
    public const string PermissionsPolicy = "Permissions-Policy";
    public const string ContentSecurityPolicy = "Content-Security-Policy";
    public const string RetryAfter = "Retry-After";
}

public static class RobotsValues
{
    /// <summary>Keeps private delivery pages out of search engines (issue #121).</summary>
    public const string NoIndexNoFollow = "noindex, nofollow";
}
