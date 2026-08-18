using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Purges security audit events older than the 90-day retention window (issue #126).
/// The trail is append-only — retention is enforced by this windowed delete.
/// </summary>
public sealed class SecurityEventCleanupJob(IServiceScopeFactory scopeFactory, ILogger<SecurityEventCleanupJob> logger)
{
    private const int RetentionDays = 90;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task CleanupAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        var purged = await db.SecurityEvents
            .Where(e => e.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);

        if (purged > 0)
            logger.LogInformation("SecurityEventCleanupJob purged {Count} security events", purged);
    }
}
