namespace QuickShare.Data.Entities;

public static class ReferralEventTypes
{
    public const string Pending = "Pending";
    public const string Unrewarded = "Unrewarded";
    public const string Rewarded = "Rewarded";
}

/// <summary>Audit trail for the referral program (issue #15). One row per referred signup.</summary>
public record ReferralEvent : BaseAuditEntity
{
    public Guid ReferrerUserId { get; set; }
    public Guid ReferredUserId { get; set; }
    public string Type { get; set; } = ReferralEventTypes.Pending;
}
