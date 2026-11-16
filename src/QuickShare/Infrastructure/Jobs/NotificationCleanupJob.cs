using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Purges in-app notifications older than the retention window (issue #246) so the
/// center doesn't grow without bound. Read state is dropped with the row.
/// </summary>
public sealed class NotificationCleanupJob(IServiceScopeFactory scopeFactory, ILogger<NotificationCleanupJob> logger)
{
    private const int RetentionDays = 30;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        var purged = await db.Notifications
            .Where(n => n.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (purged > 0)
            logger.LogInformation("NotificationCleanupJob purged {Count} notifications", purged);
    }
}
