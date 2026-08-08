namespace QuickShare.Features.Folders.Update;

public sealed class UpdateFolderEndpoint : Endpoint<UpdateFolderCommand, FolderItemResponse>
{
    public override void Configure()
    {
        Put("/api/folders/{id}");
        // SendResultOkAsync writes to the response directly; stop FastEndpoints
        // from overwriting it with a default 204.
        DontAutoSendResponse();
        Description(b => b.WithTags(ApiTags.Folders));
        Summary(s =>
        {
            s.Summary = "Update a folder";
            s.Description = "Updates the name of an existing folder. Folder names must be unique.";
            s.Params["id"] = "Unique identifier of the folder.";
            s.Responses[200] = "The updated folder.";
            s.Responses[404] = "No folder with the given id exists.";
            s.Responses[409] = "A different folder with the same name already exists.";
        });
    }

    public override async Task HandleAsync(UpdateFolderCommand req, CancellationToken ct)
    {
        await this.SendResultOkAsync(
            Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FolderItemResponse>>(req, ct),
            ct);
    }
}
