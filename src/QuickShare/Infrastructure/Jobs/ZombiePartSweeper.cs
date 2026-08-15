using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

public sealed class ZombiePartSweeper(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<ZombiePartSweeper> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task SweepZombiePartsAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var stale = await db.UploadSessions
            .Where(s => s.Status == UploadSessionStatus.InProgress && s.ExpiresAt < now && s.R2UploadId != null && s.StoragePath != null)
            .ToListAsync(ct);

        if (stale.Count == 0) return;

        foreach (var session in stale)
        {
            try
            {
                await storage.AbortR2MultipartUploadAsync(session.R2UploadId!, session.StoragePath!, ct);
                logger.LogInformation("Aborted zombie R2 upload: UploadId={UploadId}, Key={Key}",
                    session.R2UploadId, session.StoragePath);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to abort zombie R2 upload {UploadId}", session.R2UploadId);
            }

            db.UploadSessions.Remove(session);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("ZombiePartSweeper cleaned {Count} stale R2 uploads", stale.Count);
    }
}
