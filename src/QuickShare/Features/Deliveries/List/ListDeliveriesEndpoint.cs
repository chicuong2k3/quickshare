namespace QuickShare.Features.Deliveries.List;

public sealed class ListDeliveriesEndpoint : EndpointWithoutRequest
{
    private const string CursorQueryKey = "cursor";
    private const string LimitQueryKey = "limit";
    private const string StatusQueryKey = "status";
    private const string SearchQueryKey = "q";
    private const string FavoriteQueryKey = "favorite";
    private const string FolderQueryKey = "folder";

    public override void Configure()
    {
        Get("/api/deliveries");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "List the caller's sent deliveries, newest first";
            s.Description = "Keyset paged: pass the previous response's nextCursor to fetch the next page.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var cursor = Query<string>(CursorQueryKey, isRequired: false);
        var limit = Query<int?>(LimitQueryKey, isRequired: false) ?? DeliveryPaging.DefaultLimit;
        var status = Query<string>(StatusQueryKey, isRequired: false);
        var search = Query<string>(SearchQueryKey, isRequired: false);
        var favorite = Query<bool?>(FavoriteQueryKey, isRequired: false) ?? false;
        // Shared workspace folder (issue #151); omitted means the caller's whole visible list.
        var folder = Query<Guid?>(FolderQueryKey, isRequired: false);

        var result = await Resolve<IMediator>().Send<LightResults.Result<CursorPage<DeliveryItem>>>(
            new ListDeliveriesQuery(cursor, limit, status, search, favorite, FolderId: folder), ct);

        if (result.IsSuccess(out var page))
            await Send.OkAsync(page, ct);
    }
}
