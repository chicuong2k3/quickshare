namespace QuickShare.Data.Entities;

/// <summary>
/// Anonymous product-analytics event (issue #24). No PII by design:
/// never stores email, user id, or raw IP — only an anonymous visitor
/// cookie id and a country code (from the Cloudflare CF-IPCountry header).
/// </summary>
public sealed record AnalyticsEvent : BaseAuditEntity
{
    public string EventName { get; set; } = string.Empty;

    /// <summary>Anonymous visitor id from the qs_visitor cookie — never a user id.</summary>
    public Guid? VisitorId { get; set; }

    /// <summary>Two-letter country code from the CF-IPCountry header, if present.</summary>
    public string? CountryCode { get; set; }

    public string? FileName { get; set; }

    public long? FileSize { get; set; }

    public string? ContentType { get; set; }

    /// <summary>Plan tier at the time of the event (free/pro/studio/anonymous).</summary>
    public string? Plan { get; set; }
}
