using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryCleanupService(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<DeliveryCleanupService> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync()
    {
        await CleanupExpiredDeliveriesAsync();
        await CleanupTrashedDeliveriesAsync();
        await CleanupAbandonedSessionsAsync();
    }

    /// <summary>Soft-deleted rows (issue #99) are purged after this long.</summary>
    public static readonly TimeSpan TrashRetention = TimeSpan.FromHours(24);

    private async Task CleanupTrashedDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purgeBefore = DateTime.UtcNow - TrashRetention;

        // Legal hold (issue #266): a held row never leaves the trash queue and its bytes
        // are never deleted. The hold outlives the 24h grace by design.
        var trashed = await db.Deliveries
            .Where(d => d.DeletedAt != null && d.DeletedAt < purgeBefore && d.LegalHoldAt == null)
            .ToListAsync(ct);

        if (trashed.Count == 0) return;

        foreach (var delivery in trashed)
        {
            if (!string.IsNullOrEmpty(delivery.StoragePath))
            {
                try
                {
                    await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete R2 objects for trashed delivery {DeliveryId}", delivery.Id);
                }
            }
        }

        db.Deliveries.RemoveRange(trashed);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Purged {Count} trashed deliveries", trashed.Count);
    }

    /// <summary>
    /// Sender-side expired archive (issue #222): expired rows stay visible for this long
    /// after expiry before the row is purged. Bytes are already deleted at expiry by
    /// DeliveryExpiryJob, so the lingering rows cost only DB space.
    /// </summary>
    public static readonly TimeSpan ExpiredRowRetention = TimeSpan.FromDays(30);

    private async Task CleanupExpiredDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var purgeBefore = DateTime.UtcNow - ExpiredRowRetention;

        // Legal hold (issue #266): held rows stay, bytes and all, however long ago they expired.
        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < purgeBefore && d.LegalHoldAt == null)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var delivery in expired)
        {
            if (!string.IsNullOrEmpty(delivery.StoragePath))
            {
                try
                {
                    await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete R2 objects for delivery {DeliveryId}", delivery.Id);
                }
            }
        }

        db.Deliveries.RemoveRange(expired);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cleaned {Count} expired deliveries (R2: {R2Count})",
            expired.Count, expired.Count(d => !string.IsNullOrEmpty(d.StoragePath)));
    }

    private async Task CleanupAbandonedSessionsAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var abandoned = await db.UploadSessions
            .Where(s => s.Status == UploadSessionStatus.InProgress && s.ExpiresAt < now)
            .ToListAsync(ct);

        if (abandoned.Count == 0) return;

        foreach (var session in abandoned)
        {
            if (!string.IsNullOrEmpty(session.R2UploadId) && !string.IsNullOrEmpty(session.StoragePath))
            {
                try
                {
                    await storage.AbortR2MultipartUploadAsync(session.R2UploadId, session.StoragePath, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to abort R2 upload for session {SessionId}", session.Id);
                }
            }
        }

        db.UploadSessions.RemoveRange(abandoned);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cleaned {Count} abandoned upload sessions (R2: {R2Count})",
            abandoned.Count, abandoned.Count(s => !string.IsNullOrEmpty(s.R2UploadId)));
    }
}
