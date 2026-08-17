namespace QuickShare.Features.Notifications.Center;

public sealed class ListNotificationsEndpoint : EndpointWithoutRequest
{
    private const string FilterQueryKey = "filter";
    private const string CursorQueryKey = "cursor";
    private const string LimitQueryKey = "limit";

    public override void Configure()
    {
        Get("/api/notifications");
        Description(b => b.WithTags(ApiTags.Notifications));
        Summary(s =>
        {
            s.Summary = "List the caller's in-app notifications, newest first";
            s.Description = "Keyset paged. filter=unread returns only unread; default is all.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var filter = Query<string>(FilterQueryKey, isRequired: false);
        var cursor = Query<string>(CursorQueryKey, isRequired: false);
        var limit = Query<int?>(LimitQueryKey, isRequired: false) ?? NotificationPaging.DefaultLimit;

        var result = await Resolve<IMediator>().Send<LightResults.Result<CursorPage<NotificationItem>>>(
            new ListNotificationsQuery(filter, cursor, limit), ct);

        if (result.IsSuccess(out var page))
            await Send.OkAsync(page, ct);
    }
}
