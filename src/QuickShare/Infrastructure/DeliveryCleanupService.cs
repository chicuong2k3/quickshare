using Microsoft.Extensions.Options;


namespace QuickShare.Infrastructure;

public sealed class DeliveryCleanupService(IServiceScopeFactory scopeFactory, IStorageProvider storage, IOptions<QuickShareOptions> options, ILogger<DeliveryCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(options.Value.CleanupIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
                await CleanupExpiredDeliveriesAsync(stoppingToken);
                await CleanupAbandonedSessionsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "Cleanup cycle failed"); }
        }
    }

    private async Task CleanupExpiredDeliveriesAsync(CancellationToken ct)
    {
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

        var chunkRefs = await db.Chunks
            .Where(c => expired.Select(d => d.Id).Contains(c.DeliveryId))
            .Select(c => c.StorageRef)
            .ToArrayAsync(ct);

        if (chunkRefs.Length > 0)
            await storage.DeleteChunksAsync(chunkRefs, ct);

        db.Deliveries.RemoveRange(expired);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cleaned {Count} expired deliveries (R2: {R2Count}, chunks: {Chunks})",
            expired.Count, expired.Count(d => !string.IsNullOrEmpty(d.StoragePath)), chunkRefs.Length);
    }

    private async Task CleanupAbandonedSessionsAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var abandoned = await db.UploadSessions
            .Where(s => s.Status == "InProgress" && s.ExpiresAt < now)
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

        var chunkRefs = await db.Chunks
            .Where(c => abandoned.Select(s => s.Id).Contains(c.DeliveryId))
            .Select(c => c.StorageRef)
            .ToArrayAsync(ct);

        if (chunkRefs.Length > 0)
            await storage.DeleteChunksAsync(chunkRefs, ct);

        db.Chunks.RemoveRange(db.Chunks.Where(c => abandoned.Select(s => s.Id).Contains(c.DeliveryId)));
        db.UploadSessions.RemoveRange(abandoned);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Cleaned {Count} abandoned upload sessions (R2: {R2Count}, chunks: {Chunks})",
            abandoned.Count, abandoned.Count(s => !string.IsNullOrEmpty(s.R2UploadId)), chunkRefs.Length);
    }
}
