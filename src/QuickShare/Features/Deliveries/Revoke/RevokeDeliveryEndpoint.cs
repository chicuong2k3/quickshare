namespace QuickShare.Features.Deliveries.Revoke;

public sealed class RevokeDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/revoke");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Revoke a delivery link before expiry";
            s.Description = "Sender only. The link stops working immediately and the stored file is deleted; the row remains visible in the sender's list.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(
            new RevokeDeliveryCommand(id), ct);

        if (result.IsSuccess())
            await Send.OkAsync(cancellation: ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}
