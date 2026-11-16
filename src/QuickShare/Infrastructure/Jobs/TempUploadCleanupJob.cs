using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Sweeps expired share-target temp uploads (issue #14): rows past the 15-min
/// TTL are removed and their temp files deleted.
/// </summary>
public sealed class TempUploadCleanupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<TempUploadCleanupJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync()
    {
        var ct = CancellationToken.None;
        var cutoff = DateTime.UtcNow.AddMinutes(-TempUpload.TtlMinutes);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var stale = await db.TempUploads
            .AsNoTracking()
            .Where(t => t.CreatedAt < cutoff)
            .ToListAsync(ct);

        if (stale.Count == 0)
            return;

        var deletedFiles = 0;
        foreach (var temp in stale)
        {
            try
            {
                if (File.Exists(temp.TempPath))
                {
                    File.Delete(temp.TempPath);
                    deletedFiles++;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete temp upload file {Path}", temp.TempPath);
            }
        }

        await db.TempUploads
            .Where(t => t.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        logger.LogInformation("TempUploadCleanupJob removed {Count} stale share-target uploads ({Files} files)", stale.Count, deletedFiles);
    }
}
