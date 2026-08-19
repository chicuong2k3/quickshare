using System.Text.Json;
using LightResults;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Deliveries.Lock;

public record UnlockDeliveryCommand(Guid DeliveryId) : IRequest<Result>;

/// <summary>
/// Releases the active lock on a delivery (issue #308). Sender-only. Releasing
/// a lock held by someone else notifies that holder.
/// </summary>
public sealed class UnlockDeliveryHandler(AppDbContext db, IHttpContextAccessor http, AnalyticsService analytics)
    : IRequestHandler<UnlockDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(
        UnlockDeliveryCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        var now = DateTime.UtcNow;

        var activeLock = await db.FileLocks
            .FirstOrDefaultAsync(l => l.DeliveryId == delivery.Id
                && l.ReleasedAt == null
                && l.ExpiresAt > now, cancellationToken);

        // No active lock — nothing to release.
        if (activeLock is null)
            return Result.Success();

        activeLock.ReleasedAt = now;

        // Releasing a lock held by someone else — notify that holder.
        if (activeLock.LockedById != user.Id)
        {
            db.Notifications.Add(new Notification
            {
                UserId = activeLock.LockedById,
                Type = NotificationType.FileLockBroken,
                Title = delivery.FileName,
                LinkUrl = $"{RoutePaths.AccountDeliveries}/{delivery.Id}",
            });
        }

        var metadata = JsonSerializer.Serialize(new { deliveryId = delivery.Id, releasedById = user.Id });
        var userAgent = http.HttpContext!.Request.Headers.UserAgent.ToString();
        var ip = http.HttpContext!.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.FileLockReleased, userAgent, ip, cancellationToken, metadata);

        await analytics.RecordAsync(AnalyticsEvents.FileLockReleased,
            new AnalyticsEventData(FileName: delivery.FileName), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}