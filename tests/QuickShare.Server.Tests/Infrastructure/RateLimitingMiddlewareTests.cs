using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Http;
using QuickShare.Infrastructure.Middleware;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.Infrastructure;

/// <summary>
/// Escalating backoff in the rate limiter (issue #52): a client that keeps blowing
/// past the limit gets locked out for longer — 60s, then 5min, then 1h — and a clean
/// hour resets the ladder. Legit under-limit traffic never trips it.
/// </summary>
public sealed class RateLimitingMiddlewareTests
{
    private static readonly TimeSpan[] TestLadder =
    [
        TimeSpan.FromSeconds(60),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromHours(1),
    ];

    private sealed class Harness
    {
        private DateTime _now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        public int NextCalls;
        public readonly RateLimitingMiddleware Middleware;

        public Harness(int limit)
        {
            Middleware = new RateLimitingMiddleware(
                _ => { NextCalls++; return Task.CompletedTask; },
                utcNow: () => _now,
                anonymousLimit: limit,
                authenticatedLimit: limit,
                backoffLadder: TestLadder);
        }

        public void Advance(TimeSpan t) => _now = _now.Add(t);

        public async Task<HttpResponse> RequestAsync()
        {
            var context = new DefaultHttpContext
            {
                Request = { Path = "/api/ping" },
            };
            context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
            await Middleware.InvokeAsync(context);
            return context.Response;
        }

        public async Task<int> GetRetryAfterAsync()
        {
            var response = await RequestAsync();
            return int.Parse(response.Headers.RetryAfter.ToString(), CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public async Task MfaPost_SharesApiRateLimit_WhileGetRemainsAvailable()
    {
        var middleware = new RateLimitingMiddleware(_ => Task.CompletedTask, anonymousLimit: 1);
        var api = new DefaultHttpContext();
        api.Request.Path = "/api/account/totp/verify";
        api.Request.Method = HttpMethods.Post;
        api.Connection.RemoteIpAddress = IPAddress.Loopback;
        await middleware.InvokeAsync(api);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, api.Response.StatusCode);

        var post = new DefaultHttpContext();
        post.Request.Path = RoutePaths.MfaChallenge;
        post.Request.Method = HttpMethods.Post;
        post.Connection.RemoteIpAddress = IPAddress.Loopback;
        await middleware.InvokeAsync(post);
        Assert.Equal(StatusCodes.Status429TooManyRequests, post.Response.StatusCode);

        var get = new DefaultHttpContext();
        get.Request.Path = RoutePaths.MfaChallenge;
        get.Request.Method = HttpMethods.Get;
        get.Connection.RemoteIpAddress = IPAddress.Loopback;
        await middleware.InvokeAsync(get);
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, get.Response.StatusCode);
    }

    [Fact]
    public async Task UnderLimit_RequestsPassThrough()
    {
        var h = new Harness(limit: 3);

        for (var i = 0; i < 3; i++)
        {
            var response = await h.RequestAsync();
            Assert.NotEqual(StatusCodes.Status429TooManyRequests, response.StatusCode);
        }

        Assert.Equal(3, h.NextCalls);
    }

    [Fact]
    public async Task ExceedingLimit_Returns429_WithBackoff()
    {
        var h = new Harness(limit: 3);

        for (var i = 0; i < 3; i++)
            await h.RequestAsync();

        // The 4th request trips the limit → first-rung lockout.
        var blocked = await h.RequestAsync();
        Assert.Equal(StatusCodes.Status429TooManyRequests, blocked.StatusCode);
        Assert.Equal(60, int.Parse(blocked.Headers.RetryAfter.ToString(), CultureInfo.InvariantCulture));

        // A request during the lockout is also 429, with the remaining backoff.
        var duringLock = await h.RequestAsync();
        Assert.Equal(StatusCodes.Status429TooManyRequests, duringLock.StatusCode);
        Assert.InRange(int.Parse(duringLock.Headers.RetryAfter.ToString(), CultureInfo.InvariantCulture), 1, 60);
    }

    [Fact]
    public async Task RepeatedViolations_EscalateBackoff()
    {
        var h = new Harness(limit: 3);

        // Violation 1 → 60s.
        await h.RequestAsync();
        await h.RequestAsync();
        await h.RequestAsync();
        Assert.Equal(60, await h.GetRetryAfterAsync());

        // Lockout passes; violate again → 5min.
        h.Advance(TimeSpan.FromSeconds(61));
        await h.RequestAsync();
        await h.RequestAsync();
        await h.RequestAsync();
        Assert.Equal(300, await h.GetRetryAfterAsync());

        // Lockout passes; violate again → 1h (capped).
        h.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await h.RequestAsync();
        await h.RequestAsync();
        await h.RequestAsync();
        Assert.Equal(3600, await h.GetRetryAfterAsync());
    }

    [Fact]
    public async Task CleanHour_ResetsTheLadder()
    {
        var h = new Harness(limit: 3);

        // First violation.
        await h.RequestAsync();
        await h.RequestAsync();
        await h.RequestAsync();
        Assert.Equal(60, await h.GetRetryAfterAsync());

        // A full clean hour later (past the lockout AND the decay window), the client
        // is forgiven: traffic flows, and the next violation starts back at 60s.
        h.Advance(TimeSpan.FromHours(2));
        var clean = await h.RequestAsync();
        Assert.NotEqual(StatusCodes.Status429TooManyRequests, clean.StatusCode);

        await h.RequestAsync();
        await h.RequestAsync();
        Assert.Equal(60, await h.GetRetryAfterAsync());
    }
}
