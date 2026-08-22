using Hangfire;
using QuickShare.Features.Developer.Webhooks;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Sends due webhook deliveries (issues #39, #172).
///
/// Runs often and takes a bounded slice each pass. The bound matters: a single receiver that
/// times out at ten seconds could otherwise hold the whole queue behind it, so a slow endpoint
/// delays its own events rather than everybody's.
/// </summary>
public sealed class WebhookDeliveryJob(IServiceScopeFactory scopeFactory, ILogger<WebhookDeliveryJob> logger)
{
    /// <summary>Deliveries attempted per pass.</summary>
    private const int BatchSize = 50;

    /// <summary>Rows kept after they finish, so the log is useful without growing forever.</summary>
    private const int RetentionDays = 30;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();

        var now = DateTime.UtcNow;

        var due = await db.WebhookDeliveries
            .Where(d => d.Status == WebhookDeliveryStatus.Pending && d.NextAttemptAt != null && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            await PurgeAsync(db, ct);
            return;
        }

        var endpointIds = due.Select(d => d.EndpointId).Distinct().ToList();
        var endpoints = await db.WebhookEndpoints
            .Where(e => endpointIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var sent = 0;
        foreach (var delivery in due)
        {
            if (!endpoints.TryGetValue(delivery.EndpointId, out var endpoint) || endpoint.RevokedAt is not null)
            {
                // The endpoint went away while this was queued. Nothing to deliver to and nothing
                // to retry — marking it failed keeps it out of the queue and visible in the log.
                delivery.Status = WebhookDeliveryStatus.Failed;
                delivery.NextAttemptAt = null;
                delivery.CompletedAt = now;
                continue;
            }

            if (!endpoint.Enabled || endpoint.IsParked)
            {
                // Parked or switched off: leave the row pending rather than burning attempts on
                // it. Re-enabling the endpoint resumes exactly where it left off.
                delivery.NextAttemptAt = now.Add(WebhookDelivery.BackoffFor(delivery.AttemptCount));
                continue;
            }

            if (await dispatcher.AttemptAsync(delivery, endpoint, ct))
                sent++;
        }

        await db.SaveChangesAsync(ct);

        if (sent > 0)
            logger.LogInformation("WebhookDeliveryJob delivered {Sent} of {Total} due webhooks", sent, due.Count);

        await PurgeAsync(db, ct);
    }

    private static async Task PurgeAsync(AppDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-RetentionDays);
        await db.WebhookDeliveries
            .Where(d => d.CompletedAt != null && d.CompletedAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}
