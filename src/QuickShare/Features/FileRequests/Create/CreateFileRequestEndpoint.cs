namespace QuickShare.Features.FileRequests.Create;

public sealed class CreateFileRequestEndpoint : Endpoint<CreateFileRequestRequest, CreateFileRequestResponse>
{
    public override void Configure()
    {
        Post("/api/file-requests");
        Description(b => b.WithTags(ApiTags.FileRequests));
        Validator<CreateFileRequestValidator>();
        Summary(s =>
        {
            s.Summary = "Create a file request";
            s.Description = "Owner only. Returns the /r/{code} link; caps (total bytes, file count, expiry) come from the owner's plan.";
            s.Responses[200] = "File request created. Returns code, title, and the request URL.";
            s.Responses[400] = "Validation error (missing title, message too long).";
        });
    }

    public override async Task HandleAsync(CreateFileRequestRequest req, CancellationToken ct)
    {
        var cmd = new CreateFileRequestCommand(req.Title, req.Message, req.Kind);
        var result = await Resolve<IMediator>().Send<LightResults.Result<CreateFileRequestResponse>>(cmd, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 400);
    }
}

public class CreateFileRequestRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }

    /// <summary>"room" for a shared drop room (issue #17); anything else is a private request.</summary>
    public string? Kind { get; set; }
}
