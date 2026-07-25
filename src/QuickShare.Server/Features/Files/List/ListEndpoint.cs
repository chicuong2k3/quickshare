namespace QuickShare.Server.Features.Files.List;

public sealed class ListFilesEndpoint : Endpoint<ListFilesQuery, PagedResult<FileItemResponse>>
{
    public override void Configure()
    {
        Get("/api/files");
        Description(b => b.WithTags("Files"));
        Summary(s =>
        {
            s.Summary = "List files";
            s.Description = "Returns a paginated list of file metadata. Metadata is read from the local database.";
            s.Params["FolderId"] = "Filter by folder ID. Omit to list all folders.";
            s.Params["Search"] = "Case-insensitive search term matched against file names and folder paths.";
            s.Params["Sort"] = "Sort field with direction. Repeat for multi-column sort (e.g. \"createdAt desc\", \"filename\"). Default: \"createdAt\" ascending.";
            s.Params["Page"] = "Page number to return, starting at 1. Default: 1.";
            s.Params["PageSize"] = "Number of items per page. Default: 50.";
            s.Responses[200] = "A page of file metadata.";
        });
    }

    public override async Task HandleAsync(ListFilesQuery req, CancellationToken ct)
    {
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<PagedResult<FileItemResponse>>>(req, ct);
        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message);
    }
}
