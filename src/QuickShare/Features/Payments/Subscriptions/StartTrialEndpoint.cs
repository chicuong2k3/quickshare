namespace QuickShare.Features.Payments.Subscriptions;

public sealed class StartTrialEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/subscriptions/trial");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Start the one-time 7-day Pro trial";
            s.Description = "Free users only, once per account. No card required (issue #65).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var result = await SubscriptionService.StartTrialAsync(user, db, ct);

        if (result.IsSuccess(out var subscription))
        {
            var analytics = Resolve<AnalyticsService>();
            await analytics.RecordAsync(AnalyticsEvents.TrialStarted,
                new AnalyticsEventData(null, 0, null, PlanTierLimits.Pro), ct);

            await Send.OkAsync(new StartTrialResponse(
                Plan: user.Plan,
                TrialEndsAt: subscription.TrialEndsAt!.Value), ct);
        }
        else if (result.IsFailure(out var error))
        {
            ThrowError(error.Message, 409);
        }
    }
}

public record StartTrialResponse(string Plan, DateTime TrialEndsAt);
