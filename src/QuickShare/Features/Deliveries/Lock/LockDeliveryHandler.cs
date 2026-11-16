using System.Text.Json;
using LightResults;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Deliveries.Lock;

public record LockDeliveryCommand(Guid DeliveryId, int? DurationMinutes, string? Reason)
    : IRequest<Result<LockDeliveryResponse>>;

public sealed record LockDeliveryResponse(Guid LockId, Guid LockedById, DateTime ExpiresAt);

/// <summary>
/// Locks a delivery so its file cannot be replaced while a new version is being
/// prepared (issue #308). Sender-only. Self-lock is idempotent; a lock held by
/// someone else is force-broken and the previous holder is notified.
/// </summary>
public sealed class LockDeliveryHandler(AppDbContext db, IHttpContextAccessor http, AnalyticsService analytics)
    : IRequestHandler<LockDeliveryCommand, Result<LockDeliveryResponse>>
{
    public async ValueTask<Result<LockDeliveryResponse>> Handle(
        LockDeliveryCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<LockDeliveryResponse>(new LightResults.Error(ErrorCodes.NotFound));

        var durationMinutes = cmd.DurationMinutes ?? FileLockLimits.DefaultDurationMinutes;
        if (durationMinutes is < 1 or > FileLockLimits.MaxDurationMinutes)
            return Result.Failure<LockDeliveryResponse>(new LightResults.Error(ErrorCodes.LockDurationInvalid));

        var now = DateTime.UtcNow;

        var activeLock = await db.FileLocks
            .FirstOrDefaultAsync(l => l.DeliveryId == delivery.Id
                && l.ReleasedAt == null
                && l.ExpiresAt > now, cancellationToken);

        // Idempotent self-lock: the caller already holds the active lock.
        if (activeLock is not null && activeLock.LockedById == user.Id)
            return Result.Success(new LockDeliveryResponse(activeLock.Id, activeLock.LockedById, activeLock.ExpiresAt));

        // Force-break: someone else holds the lock — release it and notify them.
        if (activeLock is not null)
        {
            activeLock.ReleasedAt = now;

            db.Notifications.Add(new Notification
            {
                UserId = activeLock.LockedById,
                Type = NotificationType.FileLockBroken,
                Title = delivery.FileName,
                LinkUrl = $"{RoutePaths.AccountDeliveries}/{delivery.Id}",
            });

            var metadata = JsonSerializer.Serialize(new { deliveryId = delivery.Id, previousHolderId = activeLock.LockedById });
            var userAgent = http.HttpContext!.Request.Headers.UserAgent.ToString();
            var ip = http.HttpContext!.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
            await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.FileLockForceBroken, userAgent, ip, cancellationToken, metadata);

            await analytics.RecordAsync(AnalyticsEvents.FileLockForceBroken,
                new AnalyticsEventData(FileName: delivery.FileName), cancellationToken);
        }

        var fileLock = new FileLock
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            LockedById = user.Id,
            Reason = string.IsNullOrWhiteSpace(cmd.Reason) ? null : cmd.Reason.Trim(),
            ExpiresAt = now.AddMinutes(durationMinutes),
        };
        db.FileLocks.Add(fileLock);

        var lockMetadata = JsonSerializer.Serialize(new { deliveryId = delivery.Id, durationMinutes });
        var lockUserAgent = http.HttpContext!.Request.Headers.UserAgent.ToString();
        var lockIp = http.HttpContext!.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.FileLockAcquired, lockUserAgent, lockIp, cancellationToken, lockMetadata);

        await analytics.RecordAsync(AnalyticsEvents.FileLockAcquired,
            new AnalyticsEventData(FileName: delivery.FileName), cancellationToken);

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new LockDeliveryResponse(fileLock.Id, fileLock.LockedById, fileLock.ExpiresAt));
    }
}