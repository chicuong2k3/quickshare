namespace TeleCloud.Server.Features.TempShares.Download;

public sealed class DownloadTempShareEndpoint : Endpoint<DownloadTempShareQuery>
{
    public override void Configure()
    {
        Get("/api/temp/{id}/download");
        AllowAnonymous();
        Description(b => b.WithTags("TempShares"));
        Summary(s =>
        {
            s.Summary = "Download a temp share";
            s.Description = "Streams the file content directly from Telegram storage. Hard expiry gate: returns 410 Gone once the share has expired. Supports HTTP Range requests (206 Partial Content).";
            s.Params["id"] = "Unique identifier (Guid) of the temp share.";
            s.Responses[200] = "Full file content.";
            s.Responses[206] = "Partial file content for the requested byte range.";
            s.Responses[404] = "No share with the given id exists.";
            s.Responses[410] = "Share has expired.";
        });
    }

    public override async Task HandleAsync(DownloadTempShareQuery req, CancellationToken ct)
    {
        await Resolve<IMessageBus>().InvokeAsync(req, ct);
    }
}
