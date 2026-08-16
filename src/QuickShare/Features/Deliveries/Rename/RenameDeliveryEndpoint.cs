namespace QuickShare.Features.Deliveries.Rename;

public sealed class RenameDeliveryEndpoint : Endpoint<RenameDeliveryRequest>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/filename");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Rename a delivery's file after upload";
            s.Description = "Sender only (issue #318). Changes the display and download name; the stored object is untouched.";
        });
    }

    public override async Task HandleAsync(RenameDeliveryRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(
            new RenameDeliveryCommand(id, req.FileName), ct);

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

public class RenameDeliveryRequest
{
    public string FileName { get; set; } = string.Empty;
}
