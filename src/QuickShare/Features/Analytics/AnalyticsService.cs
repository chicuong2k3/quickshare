using Microsoft.Extensions.Options;
using QuickShare.Features.Analytics;

namespace QuickShare.Features.Analytics;

/// <summary>Non-PII metadata carried by a recorded event.</summary>
public sealed record AnalyticsEventData(
    string? FileName = null,
    long? FileSize = null,
    string? ContentType = null,
    string? Plan = null);

/// <summary>Identity context captured at request time (visitor cookie + country header).</summary>
public sealed record AnalyticsContext(Guid? VisitorId, string? CountryCode);

/// <summary>
/// Records anonymous product-analytics events (issue #24). Never stores PII —
/// only the qs_visitor cookie id and the Cloudflare CF-IPCountry code.
/// Each write uses its own short-lived scope, so it is safe to call from
/// request handlers, background tasks, and middleware alike. Failures are
/// logged and swallowed: analytics must never break a file transfer.
/// </summary>
public sealed class AnalyticsService(
    IServiceScopeFactory scopeFactory,
    IHttpContextAccessor httpContextAccessor,
    IOptions<AnalyticsOptions> options,
    ILogger<AnalyticsService> logger)
{
    private readonly AnalyticsOptions _options = options.Value;

    /// <summary>Record an event using identity derived from the current HttpContext.</summary>
    public async Task RecordAsync(string eventName, AnalyticsEventData? data = null, CancellationToken ct = default)
    {
        AnalyticsContext context;
        try
        {
            context = CaptureContext(httpContextAccessor.HttpContext);
        }
        catch
        {
            context = new AnalyticsContext(null, null);
        }

        await RecordAsync(eventName, data, context, ct);
    }

    /// <summary>Record an event with an already-captured identity (background-task safe).</summary>
    public async Task RecordAsync(string eventName, AnalyticsEventData? data, AnalyticsContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
            return;

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AnalyticsEvents.Add(new AnalyticsEvent
            {
                Id = Guid.CreateVersion7(),
                EventName = eventName,
                VisitorId = context.VisitorId,
                CountryCode = context.CountryCode,
                FileName = data?.FileName,
                FileSize = data?.FileSize,
                ContentType = data?.ContentType,
                Plan = data?.Plan
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // ponytail: analytics must never fail the primary flow.
            logger.LogWarning(ex, "Failed to record analytics event {EventName}", eventName);
        }
    }

    /// <summary>
    /// Reads (and, when missing, creates) the anonymous visitor cookie and the
    /// country code from the Cloudflare header. Returns null identity when there
    /// is no HttpContext (e.g. background job) — the event is still recorded.
    /// </summary>
    public static AnalyticsContext CaptureContext(HttpContext? http)
    {
        if (http is null)
            return new AnalyticsContext(null, null);

        var visitorId = ReadOrCreateVisitorCookie(http);

        var country = http.Request.Headers[AnalyticsConstants.CountryHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(country) && country.Length > 2)
            country = country[..2].ToUpperInvariant();

        return new AnalyticsContext(visitorId, string.IsNullOrWhiteSpace(country) ? null : country);
    }

    private static Guid? ReadOrCreateVisitorCookie(HttpContext http)
    {
        var existing = http.Request.Cookies[AnalyticsConstants.VisitorCookieName];
        if (Guid.TryParse(existing, out var parsed))
            return parsed;

        var id = Guid.NewGuid();
        http.Response.Cookies.Append(AnalyticsConstants.VisitorCookieName, id.ToString(), new CookieOptions
        {
            Path = "/",
            MaxAge = TimeSpan.FromDays(400),
            SameSite = SameSiteMode.Lax,
            HttpOnly = true,
            Secure = false
        });
        return id;
    }
}
