using Hangfire;
using QuickShare.Features.Developer.Webhooks;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Telemetry;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryExpiryJob(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<DeliveryExpiryJob> logger)
{
    private const string DownloadInfoCacheKeyPrefix = "download:info:";

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExpireDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        using var activity = ActivitySources.Source.StartActivity(ActivitySources.DeliveryExpiryActivity);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < now && d.Status == DeliveryStatus.Active)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        activity?.SetTag(ActivitySources.DeliveryCountTag, expired.Count);

        foreach (var delivery in expired)
        {
            if (!string.IsNullOrEmpty(delivery.StoragePath))
            {
                var retries = 0;
                var maxRetries = 3;
                var delay = TimeSpan.FromSeconds(5);

                while (retries < maxRetries)
                {
                    try
                    {
                        await QuickShare.Features.Uploads.Shared.DeduplicationService.ReleaseOrDeleteAsync(db, storage, delivery.StoragePath, delivery.IsDeduplicated, ct);
                        break;
                    }
                    catch
                    {
                        retries++;
                        if (retries < maxRetries)
                            await Task.Delay(delay, ct);
                        delay *= 2;
                    }
                }

                if (retries == maxRetries)
                    logger.LogWarning("Failed to delete R2 objects after {Retries} retries for delivery {DeliveryId}",
                        maxRetries, delivery.Id);
            }

            // Version history (#110): delete past version objects at expiry.
            var versions = await db.DeliveryVersions
                .Where(v => v.DeliveryId == delivery.Id)
                .ToListAsync(ct);
            foreach (var version in versions)
            {
                try
                {
                    await storage.DeleteObjectsAsync(version.StoragePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete version object for expired delivery {DeliveryId}", delivery.Id);
                }
            }
            db.DeliveryVersions.RemoveRange(versions);

            delivery.Status = DeliveryStatus.Expired;
        }

        // Outbound webhook (issue #39), alongside the in-app notification below: an
        // integration that filed the share somewhere needs to know it is gone.
        var webhooks = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
        foreach (var delivery in expired.Where(d => d.SenderId != null))
        {
            await webhooks.EnqueueAsync(delivery.SenderId, WebhookEvents.DeliveryExpired, new
            {
                deliveryId = delivery.Id,
                shareCode = delivery.ShareCode,
                fileName = delivery.FileName,
            }, ct);
        }

        // In-app notification (issue #246) for logged-in senders whose file expired.
        foreach (var delivery in expired.Where(d => d.SenderId != null))
        {
            db.Notifications.Add(new Notification
            {
                UserId = delivery.SenderId!.Value,
                Type = NotificationType.DeliveryExpired,
                Title = delivery.FileName,
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DeliveryExpiryJob expired {Count} deliveries", expired.Count);
    }
}
