

namespace QuickShare.Features.Payments.Subscriptions;

public static class SubscriptionService
{
    /// <summary>
    /// Activates or extends a subscription after a successful payment.
    /// If user has no active subscription, creates one.
    /// If user has an active subscription, extends the current period.
    /// If user has a cancelled subscription, re-activates it.
    /// </summary>
    public static async Task<Subscription> HandlePaymentAsync(
        User user,
        string plan,
        string billingPeriod,
        AppDbContext db,
        CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            subscription = CreateSubscription(user.Id, plan, billingPeriod);
            db.Subscriptions.Add(subscription);
        }
        else
        {
            subscription.Plan = plan;
            subscription.BillingPeriod = billingPeriod;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAt = null;
            subscription.UpdatedAt = DateTime.UtcNow;

            // Extend from the later of now and the current period end (no double-dipping).
            var start = subscription.CurrentPeriodEnd > DateTime.UtcNow
                ? subscription.CurrentPeriodEnd
                : DateTime.UtcNow;
            subscription.CurrentPeriodStart = start;
            subscription.CurrentPeriodEnd = AddBillingPeriod(start, billingPeriod);
        }

        user.Plan = plan;
        user.UpdatedAt = DateTime.UtcNow;

        return subscription;
    }

    /// <summary>
    /// Cancels a subscription. The plan remains active until CurrentPeriodEnd.
    /// </summary>
    public static async Task<Subscription?> CancelAsync(User user, AppDbContext db, CancellationToken ct)
    {
        var subscription = await db.Subscriptions
            .Where(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
            return null;

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.UpdatedAt = DateTime.UtcNow;

        return subscription;
    }

    /// <summary>
    /// Expires all subscriptions past their CurrentPeriodEnd.
    /// Returns count of expired subscriptions.
    /// </summary>
    public static async Task<int> ExpireStaleAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var stale = await db.Subscriptions
            .Where(s =>
                s.Status == SubscriptionStatus.Active ||
                s.Status == SubscriptionStatus.Cancelled)
            .Where(s => s.CurrentPeriodEnd <= now)
            .Include(s => s.User)
            .ToListAsync(ct);

        foreach (var sub in stale)
        {
            sub.Status = SubscriptionStatus.Expired;
            sub.UpdatedAt = now;

            if (sub.User.Plan != PlanTierLimits.Free)
            {
                sub.User.Plan = PlanTierLimits.Free;
                sub.User.UpdatedAt = now;
            }
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);

        return stale.Count;
    }

    /// <summary>
    /// Gets the user's current active or cancelled subscription.
    /// </summary>
    public static async Task<Subscription?> GetCurrentAsync(User user, AppDbContext db, CancellationToken ct)
    {
        return await db.Subscriptions
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);
    }

    private static Subscription CreateSubscription(Guid userId, string plan, string billingPeriod)
    {
        var now = DateTime.UtcNow;
        return new Subscription
        {
            UserId = userId,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            BillingPeriod = billingPeriod,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = AddBillingPeriod(now, billingPeriod),
        };
    }

    private static DateTime AddBillingPeriod(DateTime from, string billingPeriod)
    {
        return billingPeriod.ToLowerInvariant() switch
        {
            "yearly" => from.AddDays(365),
            _ => from.AddDays(30), // monthly default
        };
    }
}
