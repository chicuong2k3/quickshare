namespace QuickShare.Features.Deliveries.List;

public sealed class ListDeliveriesEndpoint : EndpointWithoutRequest
{
    private const string CursorQueryKey = "cursor";
    private const string LimitQueryKey = "limit";

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

        var result = await Resolve<IMessageBus>()
            .InvokeAsync<LightResults.Result<CursorPage<DeliveryItem>>>(new ListDeliveriesQuery(cursor, limit), ct);

        if (result.IsSuccess(out var page))
            await Send.OkAsync(page, ct);
    }
}
