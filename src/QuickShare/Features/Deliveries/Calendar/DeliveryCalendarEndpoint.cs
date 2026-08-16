namespace QuickShare.Features.Deliveries.Calendar;

public sealed class DeliveryCalendarEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}/calendar.ics");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Delivery expiry as an .ics calendar event";
            s.Description = "Anyone with the link can add the expiry to their calendar (issue #232). Includes a 24h-before reminder alarm.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<string>>(
            new DeliveryCalendarQuery(shareCode), ct);

        if (result.IsSuccess(out var ics))
        {
            HttpContext.Response.Headers.ContentDisposition = "attachment; filename=\"quickshare-expiry.ics\"";
            await Send.BytesAsync(System.Text.Encoding.UTF8.GetBytes(ics), contentType: MediaTypes.Calendar, cancellation: ct);
        }
        else
        {
            await Send.NotFoundAsync(ct);
        }
    }
}
