using LightResults;

namespace QuickShare.Features.Deliveries.Lock;

/// <summary>POST /api/deliveries/{Id}/unlock — release a delivery lock (issue #308).</summary>
public sealed class UnlockDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/unlock");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Unlock a delivery";
            s.Description = "Sender only. Releases the active lock, if any. No-op when the delivery is not locked.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");

        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new UnlockDeliveryCommand(id), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}