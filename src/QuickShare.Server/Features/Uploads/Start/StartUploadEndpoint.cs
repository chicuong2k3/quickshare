namespace QuickShare.Server.Features.Uploads.Start;

public sealed class StartUploadEndpoint : Endpoint<StartUploadCommand, StartUploadResponse>
{
    public override void Configure()
    {
        Post("/api/uploads/start");
        Description(b => b.WithTags("Uploads"));
        Summary(s =>
        {
            s.Summary = "Start a chunked upload session";
            s.Description = "Creates an upload session with 24h TTL for chunked file uploads.";
            s.Responses[201] = "Upload session created. Returns uploadId and chunkSize.";
            s.Responses[413] = "File exceeds plan limit.";
        });
    }

    public override async Task HandleAsync(StartUploadCommand req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<StartUploadResponse>>(req, ct);

        if (result.IsSuccess(out var value))
            await Send.CreatedAtAsync($"/api/uploads/{value.UploadId}", value, cancellation: ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 413);
    }
}