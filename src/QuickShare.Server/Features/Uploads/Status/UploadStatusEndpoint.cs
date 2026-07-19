namespace QuickShare.Server.Features.Uploads.Status;

public sealed class UploadStatusEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/uploads/{UploadId}/status");
        Description(b => b.WithTags("Uploads"));
        Summary(s =>
        {
            s.Summary = "Get upload session status";
            s.Description = "Returns uploaded and missing chunk indices for upload resume.";
            s.Responses[200] = "Status with chunk lists.";
            s.Responses[404] = "Upload session not found.";
            s.Responses[410] = "Session expired.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadId = Route<Guid>("UploadId");
        var query = new UploadStatusQuery(uploadId);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<UploadStatusResponse>>(query, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
        {
            if (error.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
                ThrowError(error.Message, 410);
            else
                ThrowError(error.Message, error.ToHttpStatusCode());
        }
    }
}
