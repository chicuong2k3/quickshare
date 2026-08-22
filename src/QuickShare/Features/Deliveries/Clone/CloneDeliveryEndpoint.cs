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
            s.Description = "Sender only (issue #218). Copies the stored object, resets the download counter, keeps protections and message, and applies the original expiry window from now. Pass ?folderId= to file the copy in a shared folder (issue #244).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        // Destination as a query parameter, not a body (issue #244): this endpoint has always
        // been called with no body at all, and requiring one now would break every existing
        // caller to add an optional field.
        var folderId = Query<Guid?>("folderId", isRequired: false);

        var result = await Resolve<IMediator>().Send<LightResults.Result<CloneDeliveryResponse>>(
            new CloneDeliveryCommand(id, folderId), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                // A destination that is not this delivery's to reach is "no such folder", not a
                // malformed request — the caller asked for something that does not exist for them.
                ErrorCodes.FolderNotFound => 404,
                _ => 400
            });
    }
}
