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
    public bool EmailOnDownload { get; set; } = true;

    /// <summary>
    /// Local-part of this account's private upload-by-email address (issue #104). Null until
    /// the account asks for one: an address nobody requested is a live ingest route nobody
    /// watches.
    /// </summary>
    public string? EmailIngestToken { get; set; }
    public string? ReferralCode { get; set; }
    public Guid? ReferredByUserId { get; set; }
    public long BonusQuotaBytes { get; set; }
    /// <summary>Opt-in recipient download history (#220). Default off for privacy.</summary>
    public bool TrackMyDownloads { get; set; }
    public int? ColdStorageDays { get; set; }
}
