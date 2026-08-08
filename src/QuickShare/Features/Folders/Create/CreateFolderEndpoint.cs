namespace QuickShare.Features.Folders.Create;

public sealed class CreateFolderEndpoint : Endpoint<CreateFolderCommand, FolderItemResponse>
{
    public override void Configure()
    {
        Post("/api/folders");
        // SendResultOkAsync writes to the response directly; stop FastEndpoints
        // from overwriting it with a default 204.
        DontAutoSendResponse();
        Description(b => b.WithTags("Folders"));
        Summary(s =>
        {
            s.Summary = "Create a folder";
            s.Description = "Creates a new folder with the given name. Folder names must be unique.";
            s.Responses[200] = "The created folder.";
            s.Responses[409] = "A folder with the same name already exists.";
        });
    }

    public override async Task HandleAsync(CreateFolderCommand req, CancellationToken ct)
    {
        await this.SendResultOkAsync(
            Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FolderItemResponse>>(req, ct),
            ct);
    }
}
