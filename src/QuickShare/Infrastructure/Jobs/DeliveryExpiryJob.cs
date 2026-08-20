using System.Text.Json;
using Hangfire;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;
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

        await EnforceRetentionAsync(scope.ServiceProvider, db, now, ct);

        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < now && d.Status == DeliveryStatus.Active)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        activity?.SetTag(ActivitySources.DeliveryCountTag, expired.Count);

        foreach (var delivery in expired)
        {
            // Legal hold (issue #266): a held delivery still stops being downloadable at
            // expiry, but its bytes are evidence — never delete them while the hold stands.
            if (!string.IsNullOrEmpty(delivery.StoragePath) && delivery.LegalHoldAt is null)
            {
                var retries = 0;
                var maxRetries = 3;
                var delay = TimeSpan.FromSeconds(5);

                while (retries < maxRetries)
                {
                    try
                    {
                        await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
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

            delivery.Status = DeliveryStatus.Expired;
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

    /// <summary>
    /// Converges live shares onto their owner's retention window (issue #161). Rows created
    /// before the policy existed still carry their original expiry, so the sweep clamps the
    /// stored value down instead of waiting for a re-upload. Legally held rows (#266) are
    /// exempt — a hold freezes the row, and shortening its expiry would only hand the purge
    /// an earlier date. The clamp never lands in the past (the shortest window is a day), so
    /// it pulls expiry dates in without expiring anything itself.
    /// </summary>
    private async Task EnforceRetentionAsync(IServiceProvider services, AppDbContext db, DateTime now, CancellationToken ct)
    {
        // The shortest window anyone can set — nothing expiring inside it can exceed a
        // policy, so this prefilter keeps the batch read to rows that might actually move.
        var earliestCeiling = now.AddDays(RetentionPolicy.MinDays);

        // Set-based: the owner's window rides along on the projection, so the whole batch
        // costs one round trip instead of a policy lookup per delivery.
        var candidates = await db.Deliveries
            .Where(d => d.SenderId != null
                && d.Sender!.RetentionDays != null
                && d.DeletedAt == null
                && d.LegalHoldAt == null
                && (d.Status == DeliveryStatus.Active || d.Status == DeliveryStatus.Scheduled)
                && (d.ExpiresAt == null || d.ExpiresAt > earliestCeiling))
            .Select(d => new { Delivery = d, RetentionDays = d.Sender!.RetentionDays })
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        var affectedByOwner = new Dictionary<Guid, int>();
        var dirtyShareCodes = new List<string>();

        foreach (var candidate in candidates)
        {
            if (!RetentionPolicy.Exceeds(candidate.Delivery.ExpiresAt, candidate.RetentionDays))
                continue;

            candidate.Delivery.ExpiresAt = RetentionPolicy.Clamp(candidate.Delivery.ExpiresAt, candidate.RetentionDays);
            dirtyShareCodes.Add(candidate.Delivery.ShareCode);

            var ownerId = candidate.Delivery.SenderId!.Value;
            affectedByOwner[ownerId] = affectedByOwner.GetValueOrDefault(ownerId) + 1;
        }

        if (affectedByOwner.Count == 0) return;

        await db.SaveChangesAsync(ct);

        // The download page reads the expiry off a cached snapshot — clamping without
        // evicting leaves recipients looking at a date the row no longer carries.
        var cache = services.GetRequiredService<CacheService>();
        foreach (var shareCode in dirtyShareCodes)
        {
            await cache.RemoveAsync($"{DownloadInfoCacheKeyPrefix}{shareCode}", ct);
        }

        foreach (var (ownerId, affected) in affectedByOwner)
        {
            var metadata = JsonSerializer.Serialize(new { affected });
            // A sweep has no HttpContext to attribute, so the actor is recorded as the
            // system — which is exactly what an automatic enforcement is.
            await SecurityEventService.AppendAsync(db, ownerId, SecurityEventType.RetentionEnforced,
                string.Empty, string.Empty, ct, metadata);
        }

        // One anonymous record per sweep: the event carries no owner identity, so a row per
        // owner would only duplicate it.
        var analytics = services.GetRequiredService<AnalyticsService>();
        await analytics.RecordAsync(AnalyticsEvents.RetentionEnforced, null, new AnalyticsContext(null, null), ct);

        logger.LogInformation("DeliveryExpiryJob clamped {Count} deliveries onto retention windows for {Owners} owners",
            dirtyShareCodes.Count, affectedByOwner.Count);
    }
}
