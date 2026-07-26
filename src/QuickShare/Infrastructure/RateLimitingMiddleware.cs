using System.Collections.Concurrent;

namespace QuickShare.Infrastructure;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConcurrentDictionary<string, RateWindow> _clients = new();
    private const int AnonymousLimit = 100;
    private const int AuthenticatedLimit = 500;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    public RateLimitingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var isAuthenticated = context.User?.Identity?.IsAuthenticated == true;
        var limit = isAuthenticated ? AuthenticatedLimit : AnonymousLimit;

        var now = DateTime.UtcNow;
        var window = _clients.GetOrAdd(clientIp, _ => new RateWindow(now));

        lock (window)
        {
            if (now - window.Start > Window)
            {
                window.Start = now;
                window.Count = 0;
            }

            window.Count++;

            if (window.Count > limit)
            {
                var retryAfter = (int)(Window - (now - window.Start)).TotalSeconds;
                context.Response.StatusCode = 429;
                context.Response.Headers.RetryAfter = retryAfter.ToString();
                context.Response.Headers.ContentType = "application/json";
                context.Response.WriteAsync(
                    $"{{\"error\":\"rate_limit_exceeded\",\"message\":\"Too many requests. Try again in {retryAfter} seconds.\",\"retryAfter\":{retryAfter}}}");
                return;
            }
        }

        await _next(context);
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
