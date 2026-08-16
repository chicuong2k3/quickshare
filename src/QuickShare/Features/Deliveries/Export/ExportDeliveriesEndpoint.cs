namespace QuickShare.Features.Deliveries.Export;

public sealed class ExportDeliveriesEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/deliveries/export");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Export the caller's download activity as CSV";
            s.Description = "Pro/Studio only. IPs are hashed. Days defaults to 30, capped at 365.";
        });
        DontAutoSendResponse();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var days = int.TryParse(HttpContext.Request.Query["days"], out var parsed) ? parsed : 30;
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<string>>(
            new ExportDeliveriesQuery(days), ct);

        if (result.IsSuccess(out var csv))
        {
            var filename = $"quickshare-downloads-{DateTime.UtcNow:yyyyMMdd}.csv";
            HttpContext.Response.Headers.ContentDisposition = $"attachment; filename=\"{filename}\"";
            await Send.StringAsync(csv, StatusCodes.Status200OK, "text/csv; charset=utf-8", ct);
        }
        else if (result.IsFailure(out var error))
        {
            ThrowError(error.Message, error.Message == ErrorCodes.RequiresPaidPlan
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status400BadRequest);
        }
    }
}
