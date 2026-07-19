namespace QuickShare.Server.Features.Downloads.Download;

public sealed class DownloadDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{ShareCode}");
        AllowAnonymous();
        Description(b => b.WithTags("Downloads"));
        Summary(s =>
        {
            s.Summary = "Download a delivery";
            s.Description = "Streams the file by reassembling chunks from storage. SHA-256 verified per chunk.";
            s.Responses[200] = "File streamed.";
            s.Responses[404] = "Delivery not found.";
            s.Responses[410] = "Expired or download limit reached.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var query = new DownloadDeliveryQuery(shareCode);
        await Resolve<IMessageBus>().InvokeAsync(query, ct);
    }
}
