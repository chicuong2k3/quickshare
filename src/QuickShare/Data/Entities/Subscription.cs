namespace QuickShare.Data.Entities;

public enum SubscriptionStatus
{
    Active,
    Cancelled,
    Expired
}

public enum BillingPeriod
{
    Monthly,
    Yearly
}

public static class SubscriptionStatusExtensions
{
    public static bool IsTerminal(this SubscriptionStatus status) =>
        status == SubscriptionStatus.Expired;
}

[DynamicSort]
public record Subscription : BaseAuditEntity
{
    public Guid UserId { get; set; }
    public string Plan { get; set; } = PlanTierLimits.Free;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }

    public User User { get; init; } = null!;
}
