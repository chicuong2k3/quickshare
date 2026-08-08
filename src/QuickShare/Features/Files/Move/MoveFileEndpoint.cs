namespace QuickShare.Features.Files.Move;

public sealed class MoveFileEndpoint : Endpoint<MoveFileCommand, FileItemResponse>
{
    public override void Configure()
    {
        Patch("/api/files/{fileId}/folder");
        // SendResultOkAsync writes to the response directly; stop FastEndpoints
        // from overwriting it with a default 204.
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Files));
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
        await this.SendResultOkAsync(
            Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FileItemResponse>>(req, ct),
            ct);
    }
}
