using LightResults;

namespace QuickShare.Features.Notifications.Center;

public record ListNotificationsQuery(
    string? Filter = null,
    string? Cursor = null,
    int Limit = NotificationPaging.DefaultLimit) : IRequest<Result<CursorPage<NotificationItem>>>;

public record NotificationItem(
    Guid Id,
    string Type,
    string Title,
    string? LinkUrl,
    bool IsRead,
    DateTime CreatedAt);

public static class NotificationPaging
{
    public const int DefaultLimit = 30;
    public const int MaxLimit = 100;
}

public static class NotificationFilter
{
    public const string All = "all";
    public const string Unread = "unread";
}

public sealed class ListNotificationsHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<ListNotificationsQuery, Result<CursorPage<NotificationItem>>>
{
    public async ValueTask<Result<CursorPage<NotificationItem>>> Handle(
        ListNotificationsQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var limit = Math.Clamp(query.Limit, 1, NotificationPaging.MaxLimit);
        var unreadOnly = string.Equals(query.Filter, NotificationFilter.Unread, StringComparison.OrdinalIgnoreCase);

        var source = db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == user.Id && (!unreadOnly || !n.IsRead));

        var page = await source
            .ToCursorPageAsync(query.Cursor, limit, n => new NotificationItem(
                n.Id, n.Type, n.Title, n.LinkUrl, n.IsRead, n.CreatedAt), cancellationToken);

        return Result.Success(page);
    }
}
