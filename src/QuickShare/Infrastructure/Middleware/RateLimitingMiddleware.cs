using System.Collections.Concurrent;
using System.Security.Claims;

namespace QuickShare.Infrastructure.Middleware;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateWindow> _clients = new();
    private readonly Func<DateTime> _utcNow;
    private readonly int _anonymousLimit;
    private readonly int _authenticatedLimit;
    private readonly TimeSpan[] _backoffLadder;

    private const int DefaultAnonymousLimit = 300;
    private const int DefaultAuthenticatedLimit = 1500;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ViolationDecay = TimeSpan.FromHours(1);
    private static readonly TimeSpan[] DefaultBackoffLadder =
    [
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(1),
    ];
    private const string UnknownClient = "unknown";
    private const string AnonymousPrefix = "anon:";
    private const string UserPrefix = "user:";
    private const string MissingUserId = "missing-user-id";
    private const string ApiPathPrefix = "/api";
    private const string P2PSocketPathPrefix = "/ws/connect/";

    public RateLimitingMiddleware(
        RequestDelegate next,
        Func<DateTime>? utcNow = null,
        int anonymousLimit = DefaultAnonymousLimit,
        int authenticatedLimit = DefaultAuthenticatedLimit,
        TimeSpan[]? backoffLadder = null)
    {
        _next = next;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _anonymousLimit = anonymousLimit;
        _authenticatedLimit = authenticatedLimit;
        _backoffLadder = backoffLadder ?? DefaultBackoffLadder;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldRateLimit(context.Request))
        {
            await _next(context);
            return;
        }

        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? UnknownClient;
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        var limit = isAuthenticated ? _authenticatedLimit : _anonymousLimit;
        var clientKey = BuildClientKey(context, clientIp, isAuthenticated);

        var now = _utcNow();
        var window = _clients.GetOrAdd(clientKey, _ => new RateWindow(now));

        int? retryAfter = null;

        lock (window)
        {
            // Decay: clear the escalation ladder after an hour of clean traffic, so a
            // one-off burst doesn't leave a client permanently one step from a long lock.
            if (window.Violations > 0 && now - window.LastViolationAt > ViolationDecay)
            {
                window.Violations = 0;
                window.LastViolationAt = DateTime.MinValue;
            }

            // Active lockout: reject everything until the backoff elapses.
            if (now < window.LockedUntil)
            {
                retryAfter = Math.Max(1, (int)Math.Ceiling((window.LockedUntil - now).TotalSeconds));
            }
            else
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
                SetRateLimitHeaders(context, limit, remaining, window.Start);

                if (window.Count > limit)
                {
                    window.Violations++;
                    window.LastViolationAt = now;
                    var backoff = BackoffFor(window.Violations);
                    window.LockedUntil = now + backoff;
                    // Fresh window once the lockout ends, so the offender doesn't re-trip on a stale count.
                    window.Start = now;
                    window.Count = 0;
                    retryAfter = (int)backoff.TotalSeconds;
                }
            }
        }

        if (retryAfter is { } seconds)
        {
            await WriteRateLimitResponseAsync(context, limit, seconds);
            return;
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

    private static bool ShouldRateLimit(HttpRequest request)
    {
        if (request.Path.StartsWithSegments(ApiPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (request.Path.StartsWithSegments(P2PSocketPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HttpMethods.IsPost(request.Method)
            && request.Path.StartsWithSegments(RoutePaths.MfaChallenge, StringComparison.OrdinalIgnoreCase);
    }

    private TimeSpan BackoffFor(int violations)
    {
        var index = Math.Clamp(violations - 1, 0, _backoffLadder.Length - 1);
        return _backoffLadder[index];
    }

    private static void SetRateLimitHeaders(HttpContext context, int limit, int remaining, DateTime windowStart)
    {
        var reset = new DateTimeOffset(windowStart + Window).ToUnixTimeSeconds();
        context.Response.Headers[HttpHeaders.XRateLimitLimit] = limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[HttpHeaders.XRateLimitRemaining] = remaining.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[HttpHeaders.XRateLimitReset] = reset.ToString(CultureInfo.InvariantCulture);
    }

    private static Task WriteRateLimitResponseAsync(HttpContext context, int limit, int retryAfterSeconds)
    {
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers[HttpHeaders.XRateLimitLimit] = limit.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers[HttpHeaders.XRateLimitRemaining] = "0";
        context.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        context.Response.Headers.ContentType = MediaTypes.Json;
        return context.Response.WriteAsync(
            $"{{\"error\":\"rate_limit_exceeded\",\"message\":\"Too many requests. Try again in {retryAfterSeconds} seconds.\",\"retryAfter\":{retryAfterSeconds}}}");
    }

    private sealed class RateWindow
    {
        public DateTime Start;
        public int Count;
        public int Violations;
        public DateTime LockedUntil = DateTime.MinValue;
        public DateTime LastViolationAt = DateTime.MinValue;

        public RateWindow(DateTime start)
        {
            Start = start;
        }
    }
}
