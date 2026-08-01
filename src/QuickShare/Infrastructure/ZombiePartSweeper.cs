

namespace QuickShare.Infrastructure;

public sealed class ZombiePartSweeper(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<ZombiePartSweeper> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await SweepZombiePartsAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "ZombiePartSweeper cycle failed"); }
        }
    }

    private async Task SweepZombiePartsAsync(CancellationToken ct)
    {
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
