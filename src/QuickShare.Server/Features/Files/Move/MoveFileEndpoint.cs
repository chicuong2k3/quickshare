namespace QuickShare.Server.Features.Files.Move;

public sealed class MoveFileEndpoint : Endpoint<MoveFileCommand, FileInfoResponse>
{
    public override void Configure()
    {
        Patch("/api/files/{fileId}/folder");
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "Move a file to a different folder";
            s.Description = "Updates the folder assignment for a file. The file and target folder must belong to the authenticated user.";
            s.Params["fileId"] = "Unique identifier of the file to move.";
            s.Responses[200] = "The updated file metadata.";
            s.Responses[404] = "File or target folder not found.";
            s.Responses[403] = "You do not own the file or the target folder.";
        });
    }

    public override async Task HandleAsync(MoveFileCommand req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FileInfoResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
