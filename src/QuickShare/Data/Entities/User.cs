namespace QuickShare.Data.Entities;

[DynamicSort]
public record User : BaseAuditEntity
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string AvatarUrl { get; set; } = string.Empty;
    public string Plan { get; set; } = PlanTierLimits.Free;
    /// <summary>
    /// The plan this account pays for on its own (issue #106). While a workspace seat is
    /// active, <see cref="Plan"/> carries the workspace's plan so every existing tier gate
    /// works unchanged; this column remembers what to restore when the seat goes away.
    /// Null means Plan has never been overridden by a workspace.
    /// </summary>
    public string? PersonalPlan { get; set; }
    /// <summary>
    /// Storage region this account's uploads are pinned to (issue #119). Pro picks one and every
    /// upload follows it; Studio may override per delivery. Null = the default bucket.
    /// </summary>
    public string? PreferredStorageRegion { get; set; }
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
