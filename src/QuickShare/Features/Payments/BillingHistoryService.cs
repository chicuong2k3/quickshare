using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments;

/// <summary>
/// Shared billing-history logic used by both the REST endpoint
/// (GET /api/payments/history) and the SSR billing page.
/// </summary>
public static class BillingHistoryService
{
    public static async Task<BillingHistoryResponse> GetForUserAsync(User user, AppDbContext db, CancellationToken ct)
    {
        var cache = StaticCache.Instance;
        return await cache.GetOrSetAsync(
            $"billing:history:{user.Id}",
            async ct2 =>
            {
                var events = await db.PaymentEvents
                    .AsNoTracking()
                    .Where(e => e.UserId == user.Id)
                    .OrderByDescending(e => e.CreatedAt)
                    .Select(e => new
                    {
                        e.EventType,
                        e.PlanDisplay,
                        e.Billing,
                        e.Amount,
                        e.Currency,
                        e.InvoiceId,
                        e.InvoiceUrl,
                        e.CreatedAt
                    })
                    .ToListAsync(ct2);

                var eventItems = events.Select(e => new PaymentEventItem(
                    e.EventType,
                    GetEventLabel(e.EventType, e.PlanDisplay, e.Billing),
                    e.PlanDisplay,
                    e.Billing,
                    e.Amount,
                    e.Currency,
                    e.InvoiceId,
                    e.InvoiceUrl,
                    e.CreatedAt
                )).ToList();

                var planDisplay = user.Plan switch
                {
                    PlanTierLimits.Pro => "Pro",
                    PlanTierLimits.Studio => "Studio",
                    _ => "Free"
                };

                return new BillingHistoryResponse(user.Plan, planDisplay, eventItems);
            },
            duration: TimeSpan.FromMinutes(2),
            token: ct
        );
    }

    private static string GetEventLabel(string eventType, string planDisplay, string billing)
    {
        return eventType switch
        {
            PaymentEventTypes.SubscriptionCreated => $"Subscription created — {planDisplay} {billing}",
            PaymentEventTypes.SubscriptionRenewed => $"Subscription renewed — {planDisplay} {billing}",
            PaymentEventTypes.SubscriptionCancelled => "Subscription cancelled",
            PaymentEventTypes.SubscriptionExpired => "Subscription expired",
            _ => eventType
        };
    }
}
