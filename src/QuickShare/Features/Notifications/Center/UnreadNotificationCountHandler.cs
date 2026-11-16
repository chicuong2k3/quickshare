using LightResults;
using Microsoft.EntityFrameworkCore;

namespace QuickShare.Features.Notifications.Center;

public record UnreadNotificationCountQuery : IRequest<Result<UnreadNotificationCountResponse>>;

public record UnreadNotificationCountResponse(int Count);

public sealed class UnreadNotificationCountHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<UnreadNotificationCountQuery, Result<UnreadNotificationCountResponse>>
{
    public async ValueTask<Result<UnreadNotificationCountResponse>> Handle(
        UnreadNotificationCountQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var count = await db.Notifications
            .CountAsync(n => n.UserId == user.Id && !n.IsRead, cancellationToken);

        return Result.Success(new UnreadNotificationCountResponse(count));
    }
}
