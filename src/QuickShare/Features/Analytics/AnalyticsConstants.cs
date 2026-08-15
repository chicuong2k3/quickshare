namespace QuickShare.Features.Analytics;

/// <summary>
/// Wire-format event names for product analytics (issue #24).
/// Anonymous by design — never carry PII. Centralized here so the
/// client beacon allowlist and the server-side emitters stay in sync.
/// </summary>
public static class AnalyticsEvents
{
    // Funnel: visitor → upload_started → upload_completed → download_completed
    public const string Visitor = "visitor";
    public const string UploadStarted = "upload_started";
    public const string UploadCompleted = "upload_completed";
    public const string DownloadCompleted = "download_completed";

    // P2P sessions
    public const string P2pSessionCreated = "p2p_session_created";
    public const string P2pReceiverJoined = "p2p_receiver_joined";

    // Gates / monetization
    public const string OtpPass = "otp_pass";
    public const string OtpFail = "otp_fail";
    public const string AdInterstitialViewed = "ad_interstitial_viewed";

    // Signup conversion
    public const string Signup = "signup";

    /// <summary>Events the anonymous client beacon may fire (allowlist for POST /api/analytics/track).</summary>
    public static readonly IReadOnlySet<string> ClientFireable = new HashSet<string>(StringComparer.Ordinal)
    {
        Visitor,
        AdInterstitialViewed
    };
}

/// <summary>Analytics configuration (kill switch).</summary>
public sealed class AnalyticsOptions
{
    public const string Section = "Analytics";

    /// <summary>Global on/off for event recording. Disabled = all RecordAsync calls no-op.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Cookie + header names used by the analytics pipeline.</summary>
public static class AnalyticsConstants
{
    public const string VisitorCookieName = "qs_visitor";
    public const string CountryHeader = "CF-IPCountry";
    public const string TrackRoute = "/api/analytics/track";
    public const string AdminAnalyticsRoute = "/api/admin/analytics";
}
