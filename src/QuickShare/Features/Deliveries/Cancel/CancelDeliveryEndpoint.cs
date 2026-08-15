namespace QuickShare.Features.Deliveries.Cancel;

public sealed class CancelDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/deliveries/{Id}");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Cancel a scheduled delivery";
            s.Description = "Deletes a scheduled delivery before activation. Sender only.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(new CancelDeliveryCommand(id), ct);

        if (result.IsSuccess())
            await Send.OkAsync(cancellation: ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 409);
    }
}
