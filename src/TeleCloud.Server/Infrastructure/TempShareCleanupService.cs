using Microsoft.Extensions.Options;

namespace TeleCloud.Server.Infrastructure;

public sealed class TempShareCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<QuickShareOptions> options,
    ILogger<TempShareCleanupService> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(
        Math.Max(1, options.Value.CleanupIntervalMinutes));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<TeleCloudDbContext>();
                var deleter = scope.ServiceProvider.GetRequiredService<ITempShareMessageDeleter>();

                await CleanupExpiredAsync(db, deleter, logger, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Temp share cleanup tick failed");
            }
        }
    }

    /// <summary>
    /// Testable per-tick cleanup. Removes expired <see cref="TempShare"/> rows (and their underlying
    /// <see cref="FileMetadata"/> rows, which cascade-delete the TempShares) after deleting the
    /// corresponding Telegram messages. Per-row Telegram failures are logged and skipped; the batch continues.
    /// </summary>
    public static async Task CleanupExpiredAsync(
        TeleCloudDbContext db,
        ITempShareMessageDeleter deleter,
        ILogger logger,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await db.TempShares
            .Include(t => t.File)
            .Where(t => t.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0)
            return;

        var removed = 0;
        var reclaimedBytes = 0L;
        var toRemove = new List<FileMetadata>(expired.Count);

        foreach (var share in expired)
        {
            if (share.File is null)
                continue;

            var ok = await deleter.DeleteAsync(share.File.MessageId, ct);
            if (!ok)
                continue; // keep the row; retry on the next tick

            toRemove.Add(share.File);
            reclaimedBytes += share.File.Size;
            removed++;
        }

        if (toRemove.Count > 0)
        {
            // Deleting the files row cascades to TempShares (FK OnDelete(Cascade)).
            db.Files.RemoveRange(toRemove);
            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Temp share cleanup: removed {Count} shares, reclaimed {Bytes} bytes",
            removed, reclaimedBytes);
    }
}
