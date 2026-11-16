using System.Text.Json;
using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments.Subscriptions;

public static class SubscriptionService
{
    /// <summary>
    /// Starts a one-time 7-day Pro trial (issue #65). No card required. The
    /// subscription is Active with CurrentPeriodEnd == TrialEndsAt, so the
    /// existing expiry path downgrades a non-converted trial to Free.
    /// </summary>
    public static async Task<Result<Subscription>> StartTrialAsync(User user, AppDbContext db, CancellationToken ct)
    {
        if (user.Plan != PlanTierLimits.Free)
            return Result.Failure<Subscription>(new LightResults.Error(ErrorCodes.TrialRequiresFreePlan));

        var hasEverSubscribed = await db.Subscriptions
            .AnyAsync(s => s.UserId == user.Id && s.Status != SubscriptionStatus.Expired, ct);
        if (hasEverSubscribed)
            return Result.Failure<Subscription>(new LightResults.Error(ErrorCodes.TrialAlreadyUsed));

        var now = DateTime.UtcNow;
        var trialEnd = now.AddDays(Subscription.TrialDays);
        var subscription = new Subscription
        {
            UserId = user.Id,
            Plan = PlanTierLimits.Pro,
            Status = SubscriptionStatus.Active,
            BillingPeriod = BillingPeriod.Monthly,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = trialEnd,
            TrialEndsAt = trialEnd
        };
        db.Subscriptions.Add(subscription);

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, PlanTierLimits.Pro)
                .SetProperty(u => u.UpdatedAt, now), ct);

        user.Plan = PlanTierLimits.Pro;
        user.UpdatedAt = now;

        await db.SaveChangesAsync(ct);
        await PushPlanToWorkspaceAsync(user.Id, db, ct);

        return Result.Success(subscription);
    }

    /// <summary>Days left in the current trial, or null when not on a trial.</summary>
    public static async Task<int?> GetTrialDaysLeftAsync(User user, AppDbContext db, CancellationToken ct)
    {
        var sub = await GetCurrentAsync(user, db, ct);
        if (sub?.TrialEndsAt is not { } trialEnd || trialEnd <= DateTime.UtcNow)
            return null;

        return Math.Max(1, (int)Math.Ceiling((trialEnd - DateTime.UtcNow).TotalDays));
    }

    /// <summary>
    /// Activates or extends a subscription after a successful payment.
    /// Clears any active dunning grace window (issue #140).
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

        var period = ParseBillingPeriod(billingPeriod);

        if (subscription is null)
        {
            subscription = CreateSubscription(user.Id, plan, period);
            db.Subscriptions.Add(subscription);
        }
        else
        {
            var wasDunning = subscription.DunningGraceEndsAt is not null;
            subscription.Plan = plan;
            subscription.BillingPeriod = period;
            subscription.Status = SubscriptionStatus.Active;
            subscription.CancelledAt = null;
            subscription.TrialEndsAt = null;
            subscription.DunningAttemptCount = 0;
            subscription.LastDunningAttemptAt = null;
            subscription.DunningGraceEndsAt = null;
            subscription.UpdatedAt = DateTime.UtcNow;

            var start = subscription.CurrentPeriodEnd > DateTime.UtcNow
                ? subscription.CurrentPeriodEnd
                : DateTime.UtcNow;
            subscription.CurrentPeriodStart = start;
            subscription.CurrentPeriodEnd = AddBillingPeriod(start, period);

            if (wasDunning)
            {
                db.SecurityEvents.Add(new SecurityEvent
                {
                    Id = Guid.CreateVersion7(),
                    UserId = user.Id,
                    EventType = SecurityEventType.SubscriptionDunningRecovered,
                    IpHash = string.Empty,
                    UserAgentLabel = nameof(SubscriptionService),
                    MetadataJson = JsonSerializer.Serialize(new { plan, billingPeriod }),
                });
            }
        }

        var now = DateTime.UtcNow;
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, plan)
                .SetProperty(u => u.UpdatedAt, now), ct);

        user.Plan = plan;
        user.UpdatedAt = now;

        await PushPlanToWorkspaceAsync(user.Id, db, ct);

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
    /// Places a paid active subscription past its renewal date into a 7-day dunning grace window,
    /// or advances its dunning retry schedule (day 0, day 3, day 7) before expiring it (issue #140).
    /// Trials and already-cancelled subscriptions expire immediately with no dunning.
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

        var downgradedUsers = new List<Guid>();
        var expiredCount = 0;

        foreach (var sub in stale)
        {
            var isPaidAutoRenew = sub.Status == SubscriptionStatus.Active
                && sub.TrialEndsAt is null
                && !sub.IsPaused;

            if (isPaidAutoRenew && sub.DunningGraceEndsAt is null)
            {
                // Enter 7-day dunning grace period (Attempt #1 at day 0).
                sub.DunningAttemptCount = 1;
                sub.LastDunningAttemptAt = now;
                sub.DunningGraceEndsAt = now.AddDays(Subscription.DunningGraceDays);
                sub.UpdatedAt = now;

                db.SecurityEvents.Add(new SecurityEvent
                {
                    Id = Guid.CreateVersion7(),
                    UserId = sub.UserId,
                    EventType = SecurityEventType.SubscriptionDunningStarted,
                    IpHash = string.Empty,
                    UserAgentLabel = nameof(SubscriptionService),
                    MetadataJson = JsonSerializer.Serialize(new
                    {
                        sub.Plan,
                        attempt = sub.DunningAttemptCount,
                        graceEndsAt = sub.DunningGraceEndsAt,
                    }),
                });
                continue;
            }

            if (isPaidAutoRenew && sub.DunningGraceEndsAt is { } graceEnd && graceEnd > now)
            {
                // Still inside the 7-day grace period; advance retry counter at day 3+.
                if (sub.DunningAttemptCount < 2 && sub.LastDunningAttemptAt.HasValue && (now - sub.LastDunningAttemptAt.Value).TotalDays >= 3)
                {
                    sub.DunningAttemptCount = 2;
                    sub.LastDunningAttemptAt = now;
                    sub.UpdatedAt = now;
                }
                continue;
            }

            // Either not eligible for dunning (trial / cancelled) or dunning grace elapsed (day 7): downgrade to Free.
            if (isPaidAutoRenew && sub.DunningAttemptCount < Subscription.DunningMaxAttempts)
            {
                sub.DunningAttemptCount = Subscription.DunningMaxAttempts;
                sub.LastDunningAttemptAt = now;
            }

            sub.Status = SubscriptionStatus.Expired;
            sub.UpdatedAt = now;
            expiredCount++;

            if (sub.User.Plan != PlanTierLimits.Free)
            {
                sub.User.Plan = PlanTierLimits.Free;
                sub.User.UpdatedAt = now;
            }
            downgradedUsers.Add(sub.User.Id);
        }

        if (stale.Count > 0)
            await db.SaveChangesAsync(ct);

        foreach (var userId in downgradedUsers)
        {
            await PushPlanToWorkspaceAsync(userId, db, ct);
            await UserCacheInvalidator.EvictAsync(db, userId, ct);
        }

        return expiredCount;
    }

    public static async Task<Subscription?> GetCurrentAsync(User user, AppDbContext db, CancellationToken ct)
    {
        var cache = StaticCache.Instance;
        return await cache.GetOrSetAsync(
            UserCacheInvalidator.SubscriptionKey(user.Id),
            async ct2 =>
            {
                return await db.Subscriptions
                    .Where(s => s.UserId == user.Id)
                    .OrderByDescending(s => s.CurrentPeriodEnd)
                    .FirstOrDefaultAsync(ct2);
            },
            duration: TimeSpan.FromMinutes(2),
            token: ct
        );
    }

    internal static async Task PushPlanToWorkspaceAsync(Guid ownerId, AppDbContext db, CancellationToken ct)
    {
        var workspaceId = await db.Workspaces
            .Where(w => w.OwnerId == ownerId)
            .Select(w => (Guid?)w.Id)
            .FirstOrDefaultAsync(ct);

        if (workspaceId is null)
            return;

        await new QuickShare.Features.Workspaces.Shared.WorkspaceService(db).SyncMemberPlansAsync(workspaceId.Value, ct);
    }

    private static Subscription CreateSubscription(Guid userId, string plan, BillingPeriod billingPeriod)
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

    private static DateTime AddBillingPeriod(DateTime from, BillingPeriod billingPeriod)
    {
        return billingPeriod switch
        {
            BillingPeriod.Yearly => from.AddDays(365),
            _ => from.AddDays(30),
        };
    }

    private static BillingPeriod ParseBillingPeriod(string value) =>
        Enum.TryParse<BillingPeriod>(value, ignoreCase: true, out var period)
            ? period
            : BillingPeriod.Monthly;
}
