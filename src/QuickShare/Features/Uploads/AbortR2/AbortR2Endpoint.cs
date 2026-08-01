namespace QuickShare.Features.Uploads.AbortR2;

public sealed class AbortR2Endpoint : Endpoint<AbortR2Request>
{
    public override void Configure()
    {
        Post("/api/uploads/abort-r2");
        Validator<AbortR2Validator>();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Abort an R2 multipart upload";
            s.Description = "Aborts an in-progress R2 multipart upload and removes the upload session. Uploaded parts are cleaned up by R2.";
            s.Responses[200] = "Upload aborted.";
            s.Responses[502] = "R2 upstream failure.";
        });
    }

    public override async Task HandleAsync(AbortR2Request req, CancellationToken ct)
    {
        var cmd = new AbortR2Command(req.DeliveryId, req.UploadId, req.ObjectKey);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(cmd, ct);

        if (result.IsSuccess())
            await Send.OkAsync(cancellation: ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message.Contains("R2") ? 502 : 400);
    }
}

public class AbortR2Request
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
}
