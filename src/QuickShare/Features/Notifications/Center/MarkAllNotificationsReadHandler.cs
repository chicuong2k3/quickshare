using LightResults;
using Microsoft.EntityFrameworkCore;

namespace QuickShare.Features.Notifications.Center;

public record MarkAllNotificationsReadCommand : IRequest<Result>;

public sealed class MarkAllNotificationsReadHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<MarkAllNotificationsReadCommand, Result>
{
    public async ValueTask<Result> Handle(MarkAllNotificationsReadCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        await db.Notifications
            .Where(n => n.UserId == user.Id && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);

        return Result.Success();
    }
}
