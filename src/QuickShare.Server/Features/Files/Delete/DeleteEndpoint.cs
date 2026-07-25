namespace QuickShare.Server.Features.Files.Delete;

public sealed class DeleteFileEndpoint : Endpoint<DeleteFileCommand>
{
    public override void Configure()
    {
        Delete("/api/files/{id}");
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "Delete a file";
            s.Description = "Deletes the file from storage and removes its metadata record.";
            s.Params["id"] = "Unique identifier of the file.";
            s.Responses[204] = "File was deleted successfully.";
            s.Responses[404] = "No file with the given id exists.";
        });
    }

    public override async Task HandleAsync(DeleteFileCommand req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(req, ct);
        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.ToHttpStatusCode());
    }
}
