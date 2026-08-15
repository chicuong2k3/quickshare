namespace QuickShare.Features.Deliveries.Extend;

public sealed class ExtendDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/extend");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Extend a delivery's expiry by 7 days";
            s.Description = "Sender only. Free plans can extend near/after expiry; paid plans anytime, capped at the plan maximum.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<ExtendDeliveryResponse>>(
            new ExtendDeliveryCommand(id), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}
