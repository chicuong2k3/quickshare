

using QuickShare.Features.Uploads.Shared;


namespace QuickShare.Features.Uploads.StartR2;

public sealed class StartR2Endpoint : Endpoint<StartR2Request, StartR2Response>
{
    public override void Configure()
    {
        Post("/api/uploads/start-r2");
        AllowAnonymous();
        Validator<StartR2Validator>();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Start an R2 multipart upload";
            s.Description = "Initiates an R2 multipart upload and returns presigned PUT URLs for all parts. The client uploads encrypted chunks directly to R2.";
            s.Responses[200] = "R2 upload initiated. Returns uploadId, objectKey, and presigned part URLs.";
            s.Responses[400] = "Validation error (file size, plan limit, etc).";
            s.Responses[502] = "R2 upstream failure.";
        });
    }

    public override async Task HandleAsync(StartR2Request req, CancellationToken ct)
    {
        var cmd = new StartR2Command(req.FileName, req.FileSize, req.ContentType, req.PartNumbers, req.SenderEmail, req.RequestCode);
        var result = await Resolve<IMediator>().Send<LightResults.Result<StartR2Response>>(cmd, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.FileRequestExpired or ErrorCodes.FileRequestClosed => 410,
                ErrorCodes.FileRequestNotFound => 404,
                var m when m.StartsWith(UploadErrors.R2Upstream, StringComparison.Ordinal) => 502,
                _ => 400
            });
    }
}

public class StartR2Request
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public IReadOnlyList<int> PartNumbers { get; set; } = [];
    public string? SenderEmail { get; set; }
    public string? RequestCode { get; set; }
}
