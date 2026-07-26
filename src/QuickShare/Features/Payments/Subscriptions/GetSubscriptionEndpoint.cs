using QuickShare.Data;

namespace QuickShare.Features.Payments.Subscriptions;

public sealed class GetSubscriptionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/subscriptions/current");
        Description(b => b.WithTags("Subscriptions"));
        Summary(s =>
        {
            s.Summary = "Get current subscription";
            s.Description = "Returns the user's current subscription if they have one, or 404 if on free plan.";
            s.Responses[200] = "Current subscription details.";
            s.Responses[204] = "No subscription found (free plan).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var subscription = await SubscriptionService.GetCurrentAsync(user, db, ct);

        if (subscription is null)
        {
            HttpContext.Response.StatusCode = 204;
            return;
        }

        await Send.OkAsync(new
        {
            plan = subscription.Plan,
            status = subscription.Status,
            billingPeriod = subscription.BillingPeriod,
            currentPeriodStart = subscription.CurrentPeriodStart,
            currentPeriodEnd = subscription.CurrentPeriodEnd,
            cancelledAt = subscription.CancelledAt
        }, ct);
    }
}
