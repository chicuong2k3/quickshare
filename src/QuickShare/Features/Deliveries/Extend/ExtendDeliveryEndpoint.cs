namespace QuickShare.Features.Deliveries.Extend;

public sealed class ExtendDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{DeliveryId}/extend");
        Description(b => b.WithTags("Deliveries"));
        Summary(s =>
        {
            s.Summary = "Extend delivery expiry by 24h";
            s.Responses[200] = "New expiry date.";
            s.Responses[404] = "Delivery not found.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var deliveryId = Route<Guid>("DeliveryId");
        var cmd = new ExtendDeliveryCommand(deliveryId);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<ExtendDeliveryResponse>>(cmd, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 404);
    }
}
