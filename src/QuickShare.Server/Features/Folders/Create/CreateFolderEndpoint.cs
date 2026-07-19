namespace QuickShare.Server.Features.Folders.Create;

public sealed class CreateFolderEndpoint : Endpoint<CreateFolderCommand, FolderInfoResponse>
{
    public override void Configure()
    {
        Post("/api/folders");
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
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FolderInfoResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
