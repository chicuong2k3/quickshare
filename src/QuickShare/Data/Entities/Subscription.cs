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
    public const int TrialDays = 7;
    public const int CancellationReasonMaxLength = 40;
    public const int CancellationFeedbackMaxLength = 1000;
    /// <summary>Longest a hold can run before it is resumed automatically.</summary>
    public const int MaxPauseDays = 90;

    public Guid UserId { get; set; }
    public string Plan { get; set; } = PlanTierLimits.Free;
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;
    public BillingPeriod BillingPeriod { get; set; } = BillingPeriod.Monthly;
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CancelledAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }

    /// <summary>
    /// Vacation hold (issue #229). While paused the account drops to Free and nothing is billed;
    /// the days already paid for are banked in <see cref="PausedDaysBanked"/> and handed back on
    /// resume. Pausing must not be a way to get free months, and it must not burn paid ones.
    /// </summary>
    public DateTime? PausedAt { get; set; }

    /// <summary>Whole days of paid time held while paused. Restored when the hold is lifted.</summary>
    public int PausedDaysBanked { get; set; }

    /// <summary>
    /// Why the subscriber left (issue #174). Free text is deliberately absent from the enum-like
    /// reason: a churn survey nobody can answer in their own words tells you only what you
    /// already thought to ask.
    /// </summary>
    public string? CancellationReason { get; set; }
    public string? CancellationFeedback { get; set; }

    /// <summary>Whether a save offer has already been shown, so it is offered once, not on a loop.</summary>
    public bool SaveOfferShown { get; set; }

    public bool IsPaused => PausedAt is not null;

    public User User { get; init; } = null!;
}
