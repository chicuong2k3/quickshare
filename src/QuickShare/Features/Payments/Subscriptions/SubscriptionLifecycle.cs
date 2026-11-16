namespace QuickShare.Features.Payments.Subscriptions;

/// <summary>Why somebody cancelled (issue #174). Closed set, so the answers can be counted.</summary>
public static class CancellationReasons
{
    public const string TooExpensive = "too_expensive";
    public const string NotUsingIt = "not_using_it";
    public const string MissingFeature = "missing_feature";
    public const string SwitchedProvider = "switched_provider";
    public const string TemporaryBreak = "temporary_break";
    public const string Other = "other";

    public static readonly string[] All =
        [TooExpensive, NotUsingIt, MissingFeature, SwitchedProvider, TemporaryBreak, Other];

    public static string Normalize(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return All.Contains(value) ? value : Other;
    }

    /// <summary>
    /// Whether a pause is the honest thing to offer instead of a discount.
    ///
    /// Somebody taking a break does not want money off — they want to stop paying for two months
    /// and come back. Offering them a discount is answering a question they did not ask, and it
    /// is the reason so many cancel flows feel like a fight.
    /// </summary>
    public static bool PreferPause(string reason) =>
        reason is TemporaryBreak or NotUsingIt;
}

/// <summary>
/// Pause, resume and proration (issues #229, #114).
///
/// Separate from <c>SubscriptionService</c>, which owns the states a payment moves through. These
/// are the states a *person* moves through, and keeping them apart means the payment path — the
/// one that must never be wrong — stays readable.
/// </summary>
public static class SubscriptionLifecycle
{
    /// <summary>
    /// Puts a subscription on hold (issue #229).
    ///
    /// The remaining paid days are banked rather than burned. That is the whole contract: a hold
    /// must not be a way to get free months, and it must not cost the subscriber time they have
    /// already paid for. Both failure modes are what make people distrust a pause button.
    /// </summary>
    public static bool Pause(Subscription subscription, DateTime now)
    {
        if (subscription.IsPaused || subscription.Status != SubscriptionStatus.Active)
            return false;

        var remaining = subscription.CurrentPeriodEnd - now;
        if (remaining <= TimeSpan.Zero)
            return false;

        subscription.PausedAt = now;
        // Rounded up: a subscriber with 3 hours left gets a day back, not zero. The rounding
        // error is a few hours of a service in our favour to withhold and not worth having.
        subscription.PausedDaysBanked = (int)Math.Ceiling(remaining.TotalDays);
        subscription.UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Lifts a hold, handing back exactly the days that were banked (issue #229).
    ///
    /// The new period ends at now + banked days, so time spent paused costs nothing and is not
    /// credited either. A hold is a stopped clock, not a free extension.
    /// </summary>
    public static bool Resume(Subscription subscription, DateTime now)
    {
        if (!subscription.IsPaused)
            return false;

        subscription.CurrentPeriodStart = now;
        subscription.CurrentPeriodEnd = now.AddDays(Math.Max(subscription.PausedDaysBanked, 1));
        subscription.PausedAt = null;
        subscription.PausedDaysBanked = 0;
        subscription.Status = SubscriptionStatus.Active;
        subscription.UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Unused value of the current period, in the smallest currency unit (issue #114).
    ///
    /// Charged for a month, three weeks used, upgrading today: the remaining week has already
    /// been paid for and should come off the new plan's price. Without this an upgrade is a
    /// penalty, which is why people wait until renewal — and some of them never come back to it.
    ///
    /// Credit is capped at the new amount: an upgrade can be free, never a payout.
    /// </summary>
    public static int ProrationCredit(Subscription? subscription, int paidAmount, int newAmount, DateTime now)
    {
        if (subscription is null
            || subscription.Status != SubscriptionStatus.Active
            || subscription.IsPaused
            || subscription.TrialEndsAt is not null
            || paidAmount <= 0)
        {
            return 0;
        }

        var periodLength = subscription.CurrentPeriodEnd - subscription.CurrentPeriodStart;
        var remaining = subscription.CurrentPeriodEnd - now;

        if (periodLength <= TimeSpan.Zero || remaining <= TimeSpan.Zero)
            return 0;

        var unusedFraction = Math.Clamp(remaining.TotalSeconds / periodLength.TotalSeconds, 0d, 1d);
        var credit = (int)Math.Floor(paidAmount * unusedFraction);

        return Math.Clamp(credit, 0, newAmount);
    }

    /// <summary>
    /// Whether a change of plan is an upgrade, which is the only direction proration applies to.
    ///
    /// A downgrade mid-cycle keeps the better plan until the period ends — refunding the
    /// difference would mean paying money back for a service already delivered, and every
    /// subscription product resolves that the same way for the same reason.
    /// </summary>
    public static bool IsUpgrade(string? currentPlan, string newPlan) =>
        Rank(newPlan) > Rank(currentPlan);

    private static int Rank(string? plan) => plan switch
    {
        PlanTierLimits.Studio => 3,
        PlanTierLimits.Pro => 2,
        PlanTierLimits.Free => 1,
        _ => 0,
    };
}
