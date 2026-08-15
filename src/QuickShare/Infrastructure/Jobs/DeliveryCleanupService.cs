using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryCleanupService(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<DeliveryCleanupService> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync()
    {
        await CleanupExpiredDeliveriesAsync();
        await CleanupAbandonedSessionsAsync();
    }

    private async Task CleanupExpiredDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < now)
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
