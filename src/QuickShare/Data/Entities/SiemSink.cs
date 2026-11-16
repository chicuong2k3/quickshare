namespace QuickShare.Data.Entities;

public static class SiemFormat
{
    /// <summary>One JSON object per event, in a <c>{"events":[…]}</c> envelope. Generic webhooks.</summary>
    public const string Json = "json";
    /// <summary>ArcSight Common Event Format lines, newline separated. Splunk and QRadar parse it natively.</summary>
    public const string Cef = "cef";
    /// <summary>Splunk HTTP Event Collector: newline-delimited <c>{"event":{…}}</c> objects, token in the header.</summary>
    public const string SplunkHec = "splunk_hec";

    public static readonly string[] All = [Json, Cef, SplunkHec];
}

/// <summary>
/// Where a workspace streams its audit trail (issue #191). Enterprise SOC teams do not log into
/// someone else's UI — they pull events into Splunk, Elastic or Datadog and alert there, so
/// "we have an audit log" is not the thing procurement verifies.
///
/// There is deliberately no outbox table. <c>security_events</c> is already durable and
/// append-only, which is exactly what an outbox is for; the sink only needs a cursor into it.
/// Shipping is "read forward from the cursor, POST, advance on success" — a failed batch simply
/// does not advance, so a retry re-sends it and nothing is lost. An outbox would be a second
/// copy of the same rows with its own chance to drift.
/// </summary>
public record SiemSink : BaseAuditEntity
{
    public const int EndpointMaxLength = 500;
    public const int SecretMaxLength = 200;
    public const int FormatMaxLength = 16;
    public const int ErrorMaxLength = 400;
    public const int EventFilterMaxLength = 1000;
    /// <summary>Events per POST. Bounded so one quiet week does not produce a multi-megabyte body.</summary>
    public const int BatchSize = 200;
    /// <summary>Consecutive failures before the sink is parked. Hourly retries forever help nobody.</summary>
    public const int MaxConsecutiveFailures = 10;

    public Guid WorkspaceId { get; set; }
    /// <summary>HTTPS only — an audit stream over plaintext is an audit stream someone else can read.</summary>
    public string Endpoint { get; set; } = string.Empty;
    public string Format { get; set; } = SiemFormat.Json;
    /// <summary>
    /// Shared secret. For <see cref="SiemFormat.SplunkHec"/> it is the HEC token; otherwise it
    /// keys the HMAC-SHA256 signature on every batch, so the receiver can prove we sent it.
    /// </summary>
    public string Secret { get; set; } = string.Empty;
    /// <summary>
    /// Comma-separated <see cref="SecurityEventType"/> names to ship; empty means everything.
    /// A SOC that only wants membership changes should not have to filter our volume for us.
    /// </summary>
    public string EventFilter { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Cursor into the append-only event log. Events at or before this are shipped.</summary>
    public DateTime? LastShippedAt { get; set; }
    public Guid? LastShippedEventId { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastAttemptAt { get; set; }

    public Workspace Workspace { get; init; } = null!;

    /// <summary>Parked after too many failures. Kept, not deleted — the admin needs to see why.</summary>
    public bool IsParked => ConsecutiveFailures >= MaxConsecutiveFailures;
}

/// <summary>
/// A countersigned data processing agreement (issue #184). GDPR Article 28 requires a
/// controller–processor contract, and enterprise procurement blocks the purchase without one;
/// signing online turns a two-week legal round trip into two clicks.
///
/// The row is the evidence: who signed, on behalf of which company, against which version of
/// the terms, when, and from where. Superseding a version means a new row — an agreement that
/// could be edited after signature would not be worth having.
/// </summary>
public record DpaSignature : BaseAuditEntity
{
    public const int VersionMaxLength = 16;
    public const int CompanyNameMaxLength = 200;
    public const int SignerMaxLength = 200;

    public Guid UserId { get; set; }
    /// <summary>Version of the terms that was signed, e.g. <c>1.0</c>.</summary>
    public string Version { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string SignerName { get; set; } = string.Empty;
    public string SignerEmail { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }
    /// <summary>Hashed, like every other IP this system keeps (privacy #26).</summary>
    public string IpHash { get; set; } = string.Empty;

    public User User { get; init; } = null!;
}

/// <summary>The DPA terms currently on offer. Bump on any wording change; old signatures keep their version.</summary>
public static class DpaTerms
{
    public const string CurrentVersion = "1.0";
    /// <summary>Publication date of <see cref="CurrentVersion"/>, shown on the page and the signed record.</summary>
    public static readonly DateOnly CurrentVersionDate = new(2026, 8, 21);
}
