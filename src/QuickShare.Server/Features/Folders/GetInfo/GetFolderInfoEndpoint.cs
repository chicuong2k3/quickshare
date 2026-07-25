namespace QuickShare.Server.Features.Folders.GetInfo;

public sealed class GetFolderInfoEndpoint : Endpoint<GetFolderInfoQuery, FolderItemResponse>
{
    public override void Configure()
    {
        Get("/api/folders/{id}");
        Description(b => b.WithTags("Folders"));
        Summary(s =>
        {
            s.Summary = "Get folder info";
            s.Description = "Returns metadata for a single folder by its id.";
            s.Params["id"] = "Unique identifier of the folder.";
            s.Responses[200] = "Folder metadata.";
            s.Responses[404] = "No folder with the given id exists.";
        });
    }

    public override async Task HandleAsync(GetFolderInfoQuery req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<FolderItemResponse>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            await Send.NotFoundAsync(ct);
    }
}
