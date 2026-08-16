using System.Collections.Concurrent;
using System.Security.Claims;

namespace QuickShare.Infrastructure.Middleware;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateWindow> _clients = new();
    private const int AnonymousLimit = 300;
    private const int AuthenticatedLimit = 1500;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private const string UnknownClient = "unknown";
    private const string AnonymousPrefix = "anon:";
    private const string UserPrefix = "user:";
    private const string MissingUserId = "missing-user-id";
    private const string ApiPathPrefix = "/api";
    private const string P2PSocketPathPrefix = "/ws/connect/";

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }


    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldRateLimit(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        var limit = isAuthenticated ? AuthenticatedLimit : AnonymousLimit;
        var clientKey = BuildClientKey(context, clientIp, isAuthenticated);

        var now = DateTime.UtcNow;
        var window = _clients.GetOrAdd(clientKey, _ => new RateWindow(now));

        lock (window)
        {
            if (now - window.Start > Window)
            {
                window.Start = now;
                window.Count = 0;
            }

            window.Count++;

            // Issue #143: expose the quota on every rate-limited response so API
            // consumers (SDKs) can throttle client-side instead of hitting 429 storms.
            var remaining = Math.Max(0, limit - window.Count);
            var reset = new DateTimeOffset(window.Start + Window).ToUnixTimeSeconds();
            context.Response.Headers[HttpHeaders.XRateLimitLimit] = limit.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers[HttpHeaders.XRateLimitRemaining] = remaining.ToString(CultureInfo.InvariantCulture);
            context.Response.Headers[HttpHeaders.XRateLimitReset] = reset.ToString(CultureInfo.InvariantCulture);

            if (window.Count > limit)
            {
                var retryAfter = (int)(Window - (now - window.Start)).TotalSeconds;
                context.Response.StatusCode = 429;
                context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
                context.Response.Headers[HttpHeaders.XRateLimitRemaining] = "0";
                context.Response.Headers.ContentType = MediaTypes.Json;
                context.Response.WriteAsync(
                    $"{{\"error\":\"rate_limit_exceeded\",\"message\":\"Too many requests. Try again in {retryAfter} seconds.\",\"retryAfter\":{retryAfter}}}");
                return;
            }
        }

        await _next(context);
    }

    private static string BuildClientKey(HttpContext context, string clientIp, bool isAuthenticated)
    {
        if (isAuthenticated)
        {
            var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? MissingUserId;
            return UserPrefix + userId;
        }

        var userAgent = context.Request.Headers.UserAgent.ToString();
        return AnonymousPrefix + clientIp + ":" + userAgent;
    }

    private static bool ShouldRateLimit(PathString path)
    {
        if (path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (path.StartsWithSegments(P2PSocketPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private sealed class RateWindow
    {
        public DateTime Start;
        public int Count;

        public RateWindow(DateTime start)
        {
            Start = start;
            Count = 0;
        }
    }
}
