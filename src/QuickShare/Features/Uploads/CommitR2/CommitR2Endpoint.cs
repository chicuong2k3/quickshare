

using QuickShare.Features.Uploads.Shared;


namespace QuickShare.Features.Uploads.CommitR2;

public sealed class CommitR2Endpoint : Endpoint<CommitR2Request, CommitR2Response>
{
    public override void Configure()
    {
        Post("/api/uploads/commit-r2");
        AllowAnonymous();
        Validator<CommitR2Validator>();
        Description(b => b.WithTags(ApiTags.Uploads));
        Summary(s =>
        {
            s.Summary = "Complete an R2 multipart upload";
            s.Description = "Completes the R2 multipart upload with provided part ETags and creates a Delivery record. Returns the share link.";
            s.Responses[201] = "Delivery created. Returns share code and link.";
            s.Responses[400] = "Validation error.";
            s.Responses[502] = "R2 upstream failure.";
        });
    }

    public override async Task HandleAsync(CommitR2Request req, CancellationToken ct)
    {
        var cmd = new CommitR2Command(
            req.DeliveryId, req.UploadId, req.ObjectKey,
            req.FileName, req.ContentType, req.FileSize, req.Parts, req.Otp, req.Source, req.DeliverAt);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<CommitR2Response>>(cmd, ct);

        if (result.IsSuccess(out var value))
        {
            HttpContext.Response.Headers.Location = $"/api/downloads/{value.ShareCode}";
            await Send.ResponseAsync(value, StatusCodes.Status201Created, ct);
        }
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message.StartsWith(UploadErrors.R2Upstream, StringComparison.Ordinal) ? 502 : 400);
    }
}

public class CommitR2Request
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public IReadOnlyList<R2PartEtag> Parts { get; set; } = [];
    public string? Otp { get; set; }
    public string? Source { get; set; }
    public DateTime? DeliverAt { get; set; }
}
