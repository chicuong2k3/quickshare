namespace QuickShare.Models;

public static class SubscriptionStatus
{
    public const string Active = "active";
    public const string Cancelled = "cancelled";
    public const string Expired = "expired";

    public static bool IsTerminal(string status) =>
        status is Expired;
}

[DynamicSort]
public record Subscription : BaseAuditEntity
{
    public Guid UserId { get; set; }
    public string Plan { get; set; } = Constants.PlanTierLimits.Free;
    public string Status { get; set; } = SubscriptionStatus.Active;
    public string BillingPeriod { get; set; } = "monthly";
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }

    public User User { get; init; } = null!;
}
