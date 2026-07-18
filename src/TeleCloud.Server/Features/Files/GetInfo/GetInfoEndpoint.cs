namespace TeleCloud.Server.Features.Files.GetInfo;

public sealed class GetFileInfoEndpoint : Endpoint<GetFileInfoQuery, FileInfoResponse>
{
    public override void Configure()
    {
        Get("/api/files/{id}");
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "Get file info";
            s.Description = "Returns metadata for a single file by its id.";
            s.Params["id"] = "Unique identifier of the file.";
            s.Responses[200] = "File metadata.";
            s.Responses[404] = "No file with the given id exists.";
        });
    }

    public override async Task HandleAsync(GetFileInfoQuery req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FileInfoResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            await Send.NotFoundAsync(ct);
    }
}
