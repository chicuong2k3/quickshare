namespace QuickShare.Features.FileRequests.Close;

public sealed class CloseFileRequestEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/file-requests/{Id}/close");
        Description(b => b.WithTags(ApiTags.FileRequests));
        Summary(s =>
        {
            s.Summary = "Close a file request";
            s.Description = "Owner only. Soft-closes the request so /r/{code} stops accepting submissions; existing files stay downloadable.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new CloseFileRequestCommand(id), ct);

        if (result.IsSuccess())
            await Send.OkAsync(new { Closed = true }, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}
