namespace QuickShare.Features.Deliveries.Alias;

public sealed class SetAliasEndpoint : Endpoint<SetAliasRequest>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/alias");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Set or clear a memorable alias for a delivery link";
            s.Description = "Sender only, paid plans (issue #311). Lowercase slug, 3-30 chars, globally unique; /d/{alias} then resolves like the share code. Null clears it.";
        });
    }

    public override async Task HandleAsync(SetAliasRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new SetAliasCommand(id, req.Alias), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.RequiresPaidPlan => 403,
                ErrorCodes.AliasTaken => 409,
                _ => 400
            });
    }
}

public class SetAliasRequest
{
    public string? Alias { get; set; }
}
