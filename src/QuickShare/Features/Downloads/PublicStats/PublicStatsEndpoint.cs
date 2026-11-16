namespace QuickShare.Features.Downloads.PublicStats;

public sealed class PublicStatsEndpoint : Endpoint<PublicStatsRequest, PublicStatsResponse>
{
    public override void Configure()
    {
        Get("/api/deliveries/{Code}/public-stats");
        AllowAnonymous();
        // The handler writes status codes directly; without this FastEndpoints
        // overwrites them with a default 204.
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Public aggregated download stats for a delivery";
            s.Description = "Totals, unique visitors and a 7-day timeline. Aggregated only — never per-recipient identity. Only serves deliveries with PublicStatsEnabled (and never privacy-mode deliveries).";
            s.Responses[200] = "Aggregated stats.";
            s.Responses[404] = "Not found, disabled, privacy-mode, suspended, revoked or expired.";
        });
    }

    public override async Task HandleAsync(PublicStatsRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<LightResults.Result<PublicStatsResponse>>(
            new PublicStatsQuery(req.Code), ct);

        if (result.IsSuccess(out var value))
        {
            await Send.ResponseAsync(value, StatusCodes.Status200OK, ct);
            return;
        }

        if (result.IsFailure(out var error))
        {
            HttpContext.Response.StatusCode = error.Message switch
            {
                ErrorCodes.DeliverySuspended => StatusCodes.Status403Forbidden,
                ErrorCodes.DeliveryRevoked => StatusCodes.Status403Forbidden,
                ErrorCodes.DeliveryExpired => StatusCodes.Status410Gone,
                _ => StatusCodes.Status404NotFound
            };
        }
    }
}

public class PublicStatsRequest
{
    public string Code { get; set; } = string.Empty;
}
