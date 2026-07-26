namespace QuickShare.Features.Downloads.Info;

public sealed class DownloadInfoEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/info");
        AllowAnonymous();
        Description(b => b.WithTags("Downloads"));
        Summary(s =>
        {
            s.Summary = "Get delivery metadata";
            s.Description = "Returns file info for the download page. No auth required.";
            s.Responses[200] = "Delivery metadata.";
            s.Responses[404] = "Delivery not found.";
            s.Responses[410] = "Delivery expired or download limit reached.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var query = new DownloadInfoQuery(shareCode);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<DownloadInfoResponse>>(query, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
        {
            if (error.Message.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
                error.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
                ThrowError(error.Message, 410);
            else
                ThrowError(error.Message, 404);
        }
    }
}
