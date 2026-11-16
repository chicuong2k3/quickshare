namespace QuickShare.Features.FileRequests.List;

public sealed class ListFileRequestsEndpoint : EndpointWithoutRequest
{
    private const string CursorQueryKey = "cursor";
    private const string LimitQueryKey = "limit";

    public override void Configure()
    {
        Get("/api/file-requests");
        Description(b => b.WithTags(ApiTags.FileRequests));
        Summary(s =>
        {
            s.Summary = "List the caller's file requests, newest first";
            s.Description = "Keyset paged: pass the previous response's nextCursor to fetch the next page.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var cursor = Query<string>(CursorQueryKey, isRequired: false);
        var limit = Query<int?>(LimitQueryKey, isRequired: false) ?? FileRequestPaging.DefaultLimit;

        var result = await Resolve<IMediator>().Send<LightResults.Result<CursorPage<FileRequestItem>>>(
            new ListFileRequestsQuery(cursor, limit), ct);

        if (result.IsSuccess(out var page))
            await Send.OkAsync(page, ct);
    }
}
