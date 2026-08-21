namespace QuickShare.Data.Entities;

[DynamicSort]
public record Delivery : BaseAuditEntity
{
    public const int MessageMaxLength = 500;
    public const int PasswordMaxLength = 72;

    public Guid? SenderId { get; init; }
    /// <summary>
    /// Workspace this delivery belongs to (issue #106). Stamped at commit time from the
    /// sender's active seat; null means a personal share. Team-mates see workspace rows
    /// in the shared history, never each other's personal ones.
    /// </summary>
    public Guid? WorkspaceId { get; set; }
    /// <summary>
    /// Shared workspace folder this delivery was uploaded into (issue #151); null = loose in
    /// the workspace, or a personal share. Always inside <see cref="WorkspaceId"/> — a folder
    /// from another workspace is refused at commit, not silently ignored.
    /// </summary>
    public Guid? FolderId { get; set; }
    /// <summary>
    /// Storage region the object was written to (issue #119); null = default bucket. Recorded
    /// rather than derived so the recipient badge and any compliance question can be answered
    /// without re-parsing the storage key.
    /// </summary>
    public string? StorageRegion { get; set; }
    public string? SenderEmail { get; set; }
    public bool SenderEmailConfirmed { get; set; }
    public bool SenderEmailConsent { get; set; }
    public const int FileNameMaxLength = 255;

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public long TotalBytes { get; init; }
    public int ChunkCount { get; init; }
    public string ShareCode { get; init; } = string.Empty;
    public const int AliasMinLength = 3;
    public const int AliasMaxLength = 30;

    public int MaxDownloads { get; set; }
    public int DownloadCount { get; set; }
    /// <summary>Per-day download cap (issue #227); null = no daily cap. Paid plans only.</summary>
    public int? DailyDownloadLimit { get; set; }
    /// <summary>Memorable slug usable in place of the share code (issue #311); lowercase, unique. Paid plans only.</summary>
    public string? Alias { get; set; }
    /// <summary>Sender-side star (issue #242).</summary>
    public bool IsFavorite { get; set; }
    public const int TagMaxLength = 30;
    public const int AccessRuleMaxLength = 500;
    public const int LegalHoldReasonMaxLength = 200;
    /// <summary>Optional sender label (issue #321); color derived from the text.</summary>
    public string? Tag { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? DeliverAt { get; set; }
    public string? OtpHash { get; set; }
    public string? PasswordHash { get; set; }
    public string? Message { get; set; }
    public string? Sha256 { get; set; }
    public DateTime? FirstDownloadedAt { get; set; }
    /// <summary>When the sender-expiry reminder email was sent (issue #29); null = not yet reminded.</summary>
    public DateTime? ExpiryReminderSentAt { get; set; }
    /// <summary>When the file-request digest email mentioning this delivery was sent (issue #16); null = not yet included in a digest.</summary>
    public DateTime? DigestNotifiedAt { get; set; }
    /// <summary>Whether recipients may ask for more time after expiry (issue #233). Default off for privacy.</summary>
    public bool AllowExtensionRequests { get; set; }
    /// <summary>Privacy mode (issue #187): no download tracking, no analytics, no visible counts.</summary>
    public bool PrivacyMode { get; set; }
    /// <summary>Email OTP gate (issue #291): recipient requests a code sent to their email, single-use + TTL.</summary>
    public bool EmailOtpEnabled { get; set; }
    public string? EmailOtpHash { get; set; }
    public string? EmailOtpEmail { get; set; }
    public DateTime? EmailOtpExpiresAt { get; set; }
    public DateTime? EmailOtpUsedAt { get; set; }
    public DateTime? EmailOtpLastSentAt { get; set; }
    /// <summary>Recipient email gate (issue #324): anonymous recipients must enter an email before downloading.</summary>
    public bool RequireRecipientEmail { get; set; }
    /// <summary>Public stats page (issue #294): a shareable aggregated download-stats page. Default off for privacy.</summary>
    public bool PublicStatsEnabled { get; set; }
    /// <summary>
    /// IP allowlist (issue #142): comma-separated CIDR ranges or bare addresses.
    /// Null/empty = reachable from any address. Studio-only at upload time.
    /// </summary>
    public string? AllowedIpRanges { get; set; }
    /// <summary>Country allow-list (issue #166): comma-separated ISO 3166-1 alpha-2 codes. Null = every country passes.</summary>
    public string? AllowedCountries { get; set; }
    /// <summary>Country deny-list (issue #166): comma-separated ISO 3166-1 alpha-2 codes. Evaluated after the allow-list.</summary>
    public string? BlockedCountries { get; set; }
    /// <summary>
    /// Legal hold (issue #266): freezes the row and its bytes against every deletion
    /// path — retention sweep, trash purge, expiry purge, account deletion. Null = not held.
    /// Lifting is manual only; there is no auto-expiry.
    /// </summary>
    public DateTime? LegalHoldAt { get; set; }
    /// <summary>Case id / reference the hold was placed under (issue #266).</summary>
    public string? LegalHoldReason { get; set; }
    /// <summary>Who placed the active hold (issue #266). Transitions are audited on the security timeline.</summary>
    public Guid? LegalHoldById { get; set; }
    public bool HideDownloadStats { get; set; }
    /// <summary>Soft-delete timestamp (issue #99); null = live. Purged after 24h.</summary>
    public DateTime? DeletedAt { get; set; }
    public string Status { get; set; } = DeliveryStatus.Active;
    public string Source { get; init; } = DeliverySources.Web;
    public string? StoragePath { get; set; }
    public User? Sender { get; init; }
    /// <summary>File request this delivery was submitted to (issue #16); null = normal upload. SenderId stays null for submissions.</summary>
    public Guid? FileRequestId { get; set; }
    public FileRequest? FileRequest { get; init; }
}

public static class DeliveryStatus
{
    public const string Active = "Active";
    public const string Scheduled = "Scheduled";
    public const string Expired = "Expired";
    public const string Suspended = "Suspended";
    public const string Revoked = "Revoked";
}
