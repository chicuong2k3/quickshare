namespace QuickShare.Features.Uploads.UploadFromUrl;

public sealed class UploadFromUrlEndpoint : Endpoint<UploadFromUrlRequest, UploadFromUrlResponse>
{
    public override void Configure()
    {
        Post("/api/upload-from-url");
        Description(b => b.WithTags(ApiTags.Uploads));
        Validator<UploadFromUrlValidator>();
        Summary(s =>
        {
            s.Summary = "Upload a file by fetching a remote URL (Pro/Studio)";
            s.Description = "Server pulls the URL into R2 and returns a share link. SSRF-guarded: private/reserved addresses are blocked.";
            s.Responses[200] = "File fetched and delivered. Returns the share link.";
            s.Responses[400] = "Invalid URL, fetch failure, or size over the plan limit.";
            s.Responses[403] = "Requires a Pro/Studio plan.";
        });
    }

    public override async Task HandleAsync(UploadFromUrlRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<UploadFromUrlResponse>>(
            new UploadFromUrlCommand(req.Url), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.RequiresPaidPlan => 403,
                _ => 400
            });
    }
}

public class UploadFromUrlRequest
{
    public string Url { get; set; } = string.Empty;
}
