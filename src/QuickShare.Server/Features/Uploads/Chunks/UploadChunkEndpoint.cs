namespace QuickShare.Server.Features.Uploads.Chunks;

public sealed class UploadChunkEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/uploads/{UploadId}/chunks/{ChunkIndex:int}");
        Description(b => b.WithTags("Uploads"));
        Summary(s =>
        {
            s.Summary = "Upload a chunk";
            s.Description = "Uploads a single chunk for a pending upload session. Idempotent — retrying returns alreadyExists.";
            s.Responses[200] = "Chunk stored or already exists.";
            s.Responses[404] = "Upload session not found.";
            s.Responses[410] = "Session expired.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var uploadId = Route<Guid>("UploadId");
        var chunkIndex = Route<int>("ChunkIndex");

        using var ms = new MemoryStream();
        await HttpContext.Request.Body.CopyToAsync(ms, ct);
        var data = ms.ToArray();

        var command = new UploadChunkCommand(uploadId, chunkIndex, data);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<UploadChunkResponse>>(command, ct);

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
