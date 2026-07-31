namespace QuickShare.Features.Payments;

/// <summary>
/// Shared billing-history logic used by both the REST endpoint
/// (GET /api/payments/history) and the SSR billing page.
/// </summary>
public static class BillingHistoryService
{
    public static async Task<BillingHistoryResponse> GetForUserAsync(User user, AppDbContext db, CancellationToken ct)
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
            .ToListAsync(ct);

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
            "pro" => "Pro",
            "studio" => "Studio",
            _ => "Free"
        };

        return new BillingHistoryResponse(user.Plan, planDisplay, eventItems);
    }

    private static string GetEventLabel(string eventType, string planDisplay, string billing)
    {
        return eventType switch
        {
            "subscription_created" => $"Subscription created — {planDisplay} {billing}",
            "subscription_renewed" => $"Subscription renewed — {planDisplay} {billing}",
            "subscription_cancelled" => "Subscription cancelled",
            "subscription_expired" => "Subscription expired",
            _ => eventType
        };
    }
}
