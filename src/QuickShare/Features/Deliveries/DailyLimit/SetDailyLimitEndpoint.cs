namespace QuickShare.Features.Deliveries.DailyLimit;

public sealed class SetDailyLimitEndpoint : Endpoint<SetDailyLimitRequest>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/daily-limit");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Set or clear a per-day download cap on a delivery";
            s.Description = "Sender only, paid plans (issue #227). Null clears the cap. Enforcement counts today's downloads — no extra identity logging.";
        });
    }

    public override async Task HandleAsync(SetDailyLimitRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new SetDailyLimitCommand(id, req.Limit), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.RequiresPaidPlan => 403,
                _ => 400
            });
    }
}

public class SetDailyLimitRequest
{
    public int? Limit { get; set; }
}
