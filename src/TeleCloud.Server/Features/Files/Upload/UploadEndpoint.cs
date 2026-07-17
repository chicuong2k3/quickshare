namespace TeleCloud.Server.Features.Files.Upload;

public sealed class UploadFileEndpoint : Endpoint<UploadFileRequest, FileInfoResponse>
{
    public override void Configure()
    {
        Post("/api/files");
        AllowAnonymous();
        AllowFileUploads();
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "Upload a file";
            s.Description = "Uploads a single file (multipart/form-data) to the Telegram storage channel and stores its metadata. Form field \"folderId\" sets the target folder (required).";
            s.Responses[200] = "Metadata of the uploaded file.";
            s.Responses[400] = "No file was included in the request.";
        });
    }

    public override async Task HandleAsync(UploadFileRequest req, CancellationToken ct)
    {
        if (req.File is null)
        {
            ThrowError("No file uploaded");
            return;
        }

        using var ms = new MemoryStream();
        await req.File.CopyToAsync(ms, ct);

        var command = new UploadFileCommand(ms.ToArray(), req.File.FileName, req.File.ContentType, req.FolderId);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FileInfoResponse>>(command, ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
