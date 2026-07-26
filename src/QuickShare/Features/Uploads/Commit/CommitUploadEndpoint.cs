namespace QuickShare.Features.Uploads.Commit;

public sealed class CommitUploadEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/uploads/{UploadId}/commit");
        Description(b => b.WithTags("Uploads"));
        Summary(s =>
        {
            s.Summary = "Commit a completed upload";
            s.Description = "Atomically creates Delivery + Manifest and links all chunks. Returns shareId and shareLink.";
            s.Responses[200] = "Upload committed. Returns share link.";
            s.Responses[400] = "Incomplete upload — missing chunks.";
            s.Responses[404] = "Upload session not found.";
            s.Responses[410] = "Session expired.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadId = Route<Guid>("UploadId");
        var command = new CommitUploadCommand(uploadId);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<CommitUploadResponse>>(command, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
        {
            if (error.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
                ThrowError(error.Message, 410);
            else if (error.Message.Contains("Incomplete", StringComparison.OrdinalIgnoreCase))
                ThrowError(error.Message, 400);
            else
                ThrowError(error.Message, error.ToHttpStatusCode());
        }
    }
}
