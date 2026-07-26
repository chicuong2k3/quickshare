namespace QuickShare.Features.Payments.Kofi;

public sealed class GetBillingHistoryEndpoint : EndpointWithoutRequest<BillingHistoryResponse>
{
    public override void Configure()
    {
        Get("/api/payments/history");
        Description(b => b.WithTags("Payments"));
        Summary(s =>
        {
            s.Summary = "Get payment history for current user";
            s.Description = "Returns the current user's plan and their payment event history for the billing page.";
            s.Responses[200] = "Billing history with plan and events.";
            s.Responses[401] = "User is not authenticated.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);

        var response = await BillingHistoryService.GetForUserAsync(user, db, ct);

        await Send.OkAsync(response, ct);
    }
}
