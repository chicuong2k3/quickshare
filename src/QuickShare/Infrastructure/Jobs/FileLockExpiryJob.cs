using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Releases expired file locks (issue #308). A lock that outlived its TTL is
/// stale — the holder is no longer actively replacing, so the delivery must not
/// stay blocked. The release is recorded (analytics only; no security event —
/// there is no HttpContext to attribute it to).
/// </summary>
public sealed class FileLockExpiryJob(IServiceScopeFactory scopeFactory, ILogger<FileLockExpiryJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ReleaseExpiredLocksAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.FileLocks
            .Where(l => l.ReleasedAt == null && l.ExpiresAt <= now)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        foreach (var fileLock in expired)
            fileLock.ReleasedAt = now;

        await db.SaveChangesAsync(ct);

        var analytics = scope.ServiceProvider.GetRequiredService<AnalyticsService>();
        foreach (var fileLock in expired)
        {
            var delivery = await db.Deliveries.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == fileLock.DeliveryId, ct);
            await analytics.RecordAsync(AnalyticsEvents.FileLockExpired,
                new AnalyticsEventData(FileName: delivery?.FileName), ct);
        }

        logger.LogInformation("FileLockExpiryJob released {Count} expired locks", expired.Count);
    }
}