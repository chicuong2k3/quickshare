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

    // Server-side remote fetch (issue #57) — a Pro user pulled a URL into R2.
    public const string UploadFromUrl = "upload_from_url";

    // P2P sessions
    public const string P2pSessionCreated = "p2p_session_created";
    public const string P2pReceiverJoined = "p2p_receiver_joined";

    // Gates / monetization
    public const string OtpPass = "otp_pass";
    public const string OtpFail = "otp_fail";
    public const string AdInterstitialViewed = "ad_interstitial_viewed";

    // Anti-bot (issue #175)
    public const string CaptchaRequired = "captcha_required";
    public const string CaptchaPassed = "captcha_passed";

    // Signup conversion
    public const string Signup = "signup";

    // Pro trial (issue #65)
    public const string TrialStarted = "trial_started";

    // Roadmap vote (issue #58) — anonymous aggregate feedback signal.
    public const string RoadmapVote = "roadmap_vote";

    // Diff preview (issue #248) — anonymous signal that a patch was rendered.
    public const string DiffViewed = "diff_view";

    // In-product feedback (issue #35) — anonymous sentiment.
    public const string FeedbackSubmitted = "feedback_submitted";

    // File lock / checkout (issue #308)
    public const string FileLockAcquired = "file_lock_acquired";
    public const string FileLockReleased = "file_lock_released";
    public const string FileLockForceBroken = "file_lock_force_broken";
    public const string FileLockExpired = "file_lock_expired";
    public const string ReplaceBlocked = "replace_blocked";

    // Delivery scope control (issues #142 IP allowlist, #166 country rules)
    public const string DeliveryAccessBlockedIp = "delivery_access_blocked_ip";
    public const string DeliveryAccessBlockedCountry = "delivery_access_blocked_country";

    // Compliance lifecycle (issues #266 legal hold, #161 retention policy)
    public const string LegalHoldPlaced = "legal_hold_placed";
    public const string LegalHoldLifted = "legal_hold_lifted";
    public const string RetentionPolicyChanged = "retention_policy_changed";
    public const string RetentionEnforced = "retention_enforced";

    /// <summary>Events the anonymous client beacon may fire (allowlist for POST /api/analytics/track).</summary>
    public static readonly IReadOnlySet<string> ClientFireable = new HashSet<string>(StringComparer.Ordinal)
    {
        Visitor,
        AdInterstitialViewed,
        RoadmapVote,
        DiffViewed
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
