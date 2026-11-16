using Microsoft.Extensions.Localization;
using QuickShare.Infrastructure.Caching;
using System.Globalization;

namespace QuickShare.Features.Payments;

/// <summary>
/// Shared billing-history logic used by the SSR billing page.
/// </summary>
public static class BillingHistoryService
{
    public static async Task<BillingHistoryResponse> GetForUserAsync(User user, AppDbContext db, IStringLocalizer<SharedLoc> loc, CancellationToken ct)
    {
        var cache = StaticCache.Instance;
        return await cache.GetOrSetAsync(
            UserCacheInvalidator.BillingHistoryKey(user.Id, CultureInfo.CurrentCulture.Name),
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
                    GetEventLabel(loc, e.EventType, e.PlanDisplay, e.Billing),
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

    private static string GetEventLabel(IStringLocalizer<SharedLoc> loc, string eventType, string planDisplay, string billing)
    {
        return eventType switch
        {
            PaymentEventTypes.SubscriptionCreated => loc["billing_evt_created", planDisplay, billing].Value,
            PaymentEventTypes.SubscriptionRenewed => loc["billing_evt_renewed", planDisplay, billing].Value,
            PaymentEventTypes.SubscriptionCancelled => loc["billing_evt_cancelled"].Value,
            PaymentEventTypes.SubscriptionExpired => loc["billing_evt_expired"].Value,
            _ => eventType
        };
    }
}
