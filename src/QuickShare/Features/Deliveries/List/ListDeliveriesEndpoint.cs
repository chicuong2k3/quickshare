namespace QuickShare.Features.Deliveries.List;

public sealed class ListDeliveriesEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/deliveries");
        Description(b => b.WithTags("Deliveries"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<DeliveryItem[]>>(new ListDeliveriesQuery(), ct);
        if (result.IsSuccess(out var items))
            await Send.OkAsync(items, ct);
    }
}
