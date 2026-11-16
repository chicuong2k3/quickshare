using LightResults;
using Microsoft.EntityFrameworkCore;

namespace QuickShare.Features.Notifications.Center;

public record MarkNotificationReadCommand(Guid NotificationId) : IRequest<Result>;

public sealed class MarkNotificationReadHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<MarkNotificationReadCommand, Result>
{
    public async ValueTask<Result> Handle(MarkNotificationReadCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var updated = await db.Notifications
            .Where(n => n.Id == cmd.NotificationId && n.UserId == user.Id && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);

        if (updated == 0)
        {
            var exists = await db.Notifications
                .AnyAsync(n => n.Id == cmd.NotificationId && n.UserId == user.Id, cancellationToken);
            return exists ? Result.Success() : Result.Failure(new LightResults.Error(ErrorCodes.NotificationNotFound));
        }

        return Result.Success();
    }
}
