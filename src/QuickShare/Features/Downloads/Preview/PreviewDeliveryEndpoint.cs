namespace QuickShare.Features.Downloads.Preview;

public sealed class PreviewDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/preview");
        AllowAnonymous();
        // The handler writes status/redirect directly to the response;
        // without this FastEndpoints overwrites it with a default 204.
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Inline media preview for a delivery";
            s.Description = "Redirects to the object with a short-lived URL. Never counts a download. Only for ungated deliveries — password/OTP-protected content is never previewed.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<Uri>>(
            new PreviewDeliveryQuery(shareCode), ct);

        if (result.IsSuccess(out var url))
        {
            HttpContext.Response.Redirect(url.AbsoluteUri, permanent: false);
            return;
        }

        if (result.IsFailure(out var error))
        {
            HttpContext.Response.StatusCode = error.Message switch
            {
                ErrorCodes.NotFound => StatusCodes.Status404NotFound,
                ErrorCodes.DeliveryGated or ErrorCodes.DownloadLimitReached => StatusCodes.Status403Forbidden,
                _ => StatusCodes.Status410Gone
            };
        }
    }
}
