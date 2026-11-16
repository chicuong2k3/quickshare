namespace QuickShare.Features.FileRequests.Get;

public sealed class GetFileRequestEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/file-requests/{Code}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.FileRequests));
        Summary(s =>
        {
            s.Summary = "Get a file request by code";
            s.Description = "Public. Powers the /r/{code} page. Returns 410 Gone when the request is expired or closed.";
            s.Responses[200] = "Active file request with current usage.";
            s.Responses[404] = "Request not found.";
            s.Responses[410] = "Request expired or closed.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var code = Route<string>("Code") ?? string.Empty;
        var result = await Resolve<IMediator>().Send<LightResults.Result<GetFileRequestResponse>>(
            new GetFileRequestQuery(code), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.FileRequestExpired or ErrorCodes.FileRequestClosed => 410,
                _ => 404
            });
    }
}
