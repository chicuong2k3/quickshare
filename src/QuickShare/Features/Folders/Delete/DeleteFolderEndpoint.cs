namespace QuickShare.Features.Folders.Delete;

public sealed class DeleteFolderEndpoint : Endpoint<DeleteFolderCommand>
{
    public override void Configure()
    {
        Delete("/api/folders/{id}");
        Description(b => b.WithTags("Folders"));
        Summary(s =>
        {
            s.Summary = "Delete a folder";
            s.Description = "Deletes a folder. The folder must be empty (no files) to be deleted.";
            s.Params["id"] = "Unique identifier of the folder.";
            s.Responses[204] = "Folder was deleted successfully.";
            s.Responses[404] = "No folder with the given id exists.";
            s.Responses[409] = "The folder still contains files.";
        });
    }

    public override async Task HandleAsync(DeleteFolderCommand req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(req, ct);
        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.ToHttpStatusCode());
    }
}
