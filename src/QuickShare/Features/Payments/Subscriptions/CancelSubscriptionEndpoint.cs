using QuickShare.Data;

namespace QuickShare.Features.Payments.Subscriptions;

public sealed class CancelSubscriptionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/subscriptions/cancel");
        Description(b => b.WithTags("Subscriptions"));
        Summary(s =>
        {
            s.Summary = "Cancel current subscription";
            s.Description = "Marks the user's active subscription as cancelled. The plan remains active until the end of the current billing period.";
            s.Responses[200] = "Subscription cancelled.";
            s.Responses[204] = "No active subscription found.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var subscription = await SubscriptionService.CancelAsync(user, db, ct);

        await db.SaveChangesAsync(ct);

        if (subscription is null)
        {
            HttpContext.Response.StatusCode = 204;
            return;
        }

        var logger = Resolve<ILogger<CancelSubscriptionEndpoint>>();
        logger.LogInformation(
            "Subscription cancelled for user={UserId} plan={Plan} expires={ExpiresAt}",
            user.Id, subscription.Plan, subscription.CurrentPeriodEnd);

        await Send.OkAsync(new
        {
            status = "cancelled",
            plan = subscription.Plan,
            expiresAt = subscription.CurrentPeriodEnd
        }, ct);
    }
}
