namespace QuickShare.Features.Deliveries.Message;

public sealed class UpdateDeliveryMessageEndpoint : Endpoint<UpdateDeliveryMessageRequest>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/message");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Update the sender note on a delivery";
            s.Description = "Editable only before the first download (issue #137). Sender only.";
        });
    }

    public override async Task HandleAsync(UpdateDeliveryMessageRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(
            new UpdateDeliveryMessageCommand(id, req.Message), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.DeliveryAlreadyDownloaded => 409,
                _ => 400
            });
    }
}

public class UpdateDeliveryMessageRequest
{
    public string? Message { get; set; }
}
