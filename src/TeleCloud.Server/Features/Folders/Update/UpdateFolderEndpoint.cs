namespace TeleCloud.Server.Features.Folders.Update;

public sealed class UpdateFolderEndpoint : Endpoint<UpdateFolderCommand, FolderInfoResponse>
{
    public override void Configure()
    {
        Put("/api/folders/{id}");
        AllowAnonymous();
        Description(b => b.WithTags("Folders"));
        Summary(s =>
        {
            s.Summary = "Update a folder";
            s.Description = "Updates the path of an existing folder. Folder paths must be unique.";
            s.Params["id"] = "Unique identifier of the folder.";
            s.Responses[200] = "The updated folder.";
            s.Responses[404] = "No folder with the given id exists.";
            s.Responses[409] = "A different folder with the same path already exists.";
        });
    }

    public override async Task HandleAsync(UpdateFolderCommand req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FolderInfoResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
