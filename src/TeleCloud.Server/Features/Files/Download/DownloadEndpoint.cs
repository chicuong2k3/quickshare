namespace TeleCloud.Server.Features.Files.Download;

public sealed class DownloadFileEndpoint : Endpoint<DownloadFileQuery>
{
    public override void Configure()
    {
        Get("/api/files/{id}/download");
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "Download a file";
            s.Description = "Streams the file content directly from Telegram storage. Supports HTTP range requests (206 Partial Content) for resumable downloads and media streaming.";
            s.Params["id"] = "Unique identifier of the file.";
            s.Params["Stream"] = "If true, serves the file inline (for browser playback); otherwise sends it as an attachment with a Content-Disposition filename. Default: false.";
            s.Params["RangeHeader"] = "Optional HTTP Range value, e.g. \"bytes=0-1048575\". When provided, the response is 206 with Content-Range and only the requested byte range.";
            s.Responses[200] = "Full file content.";
            s.Responses[206] = "Partial file content for the requested byte range.";
            s.Responses[404] = "No file with the given id exists.";
        });
    }

    public override async Task HandleAsync(DownloadFileQuery req, CancellationToken ct)
    {
        await Resolve<IMessageBus>().InvokeAsync(req, ct);
    }
}
