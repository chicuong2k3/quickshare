namespace QuickShare.Features.Deliveries.Clone;

public sealed class CloneDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/clone");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Clone a delivery into a fresh link";
            s.Description = "Sender only (issue #218). Copies the stored object, resets the download counter, keeps protections and message, and applies the original expiry window from now.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result<CloneDeliveryResponse>>(
            new CloneDeliveryCommand(id), ct);

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
