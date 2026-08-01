namespace QuickShare.Features.Downloads.Download;

public sealed class DownloadDeliveryEndpoint : Endpoint<DownloadDeliveryRequest>
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Download a delivery";
            s.Description = "Redirects to a presigned R2 download URL. Requires OTP if delivery is OTP-protected. Full downloads get 7-day presigned URL and increment counter; Range requests get 1-hour URL without counter increment.";
            s.Responses[302] = "Redirect to presigned R2 download URL.";
            s.Responses[404] = "Delivery not found.";
            s.Responses[410] = "Delivery expired.";
            s.Responses[403] = "Download limit reached or invalid OTP.";
        });
    }

    public override async Task HandleAsync(DownloadDeliveryRequest req, CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var query = new DownloadDeliveryQuery(shareCode, req.Otp);
        await Resolve<IMessageBus>().InvokeAsync(query, ct);
    }
}

public class DownloadDeliveryRequest
{
    public string? Otp { get; set; }
}
