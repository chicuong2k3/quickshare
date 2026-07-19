namespace QuickShare.Server.Features.Folders.List;

public sealed class ListFoldersEndpoint : Endpoint<ListFoldersQuery, PagedResult<FolderInfoResponse>>
{
    public override void Configure()
    {
        Get("/api/folders");
        Description(b => b.WithTags("Folders"));
        Summary(s =>
        {
            s.Summary = "List folders";
            s.Description = "Returns a paginated list of all folders.";
            s.Params["Sort"] = "Sort field with direction. Repeat for multi-column sort (e.g. \"path asc\", \"createdAt desc\"). Default: \"createdAt\" ascending.";
            s.Params["Page"] = "Page number to return, starting at 1. Default: 1.";
            s.Params["PageSize"] = "Number of items per page. Default: 50.";
            s.Responses[200] = "A page of folders.";
        });
    }

    public override async Task HandleAsync(ListFoldersQuery req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<PagedResult<FolderInfoResponse>>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
