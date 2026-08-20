namespace QuickShare.Data.Entities;

[DynamicSort]
public record User : BaseAuditEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Plan { get; set; } = PlanTierLimits.Free;
    public bool EmailOnDownload { get; set; } = true;
    public string? ReferralCode { get; set; }
    public Guid? ReferredByUserId { get; set; }
    public long BonusQuotaBytes { get; set; }
    /// <summary>Opt-in recipient download history (#220). Default off for privacy.</summary>
    public bool TrackMyDownloads { get; set; }
    /// <summary>
    /// Retention policy (issue #161): every share this account creates expires within N days,
    /// and the expiry sweep enforces the window on rows that predate the policy.
    /// Null = no organizational policy (per-delivery expiry only).
    /// </summary>
    public int? RetentionDays { get; set; }
    /// <summary>Regulated mode (issue #161): extends may never push a share past the retention window.</summary>
    public bool RetentionNoExtend { get; set; }
}
