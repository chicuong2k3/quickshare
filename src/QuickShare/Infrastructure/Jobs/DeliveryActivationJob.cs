using Hangfire;
using QuickShare.Infrastructure.Telemetry;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryActivationJob(IServiceScopeFactory scopeFactory, ILogger<DeliveryActivationJob> logger)
{
    // Never overlap — preserve the sequential semantics of the old BackgroundService loop.
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    // A failed poll is retried on the next cron tick, not retried in-place.
    [AutomaticRetry(Attempts = 0)]
    public async Task ActivateDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        using var activity = ActivitySources.Source.StartActivity(ActivitySources.DeliveryActivationActivity);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var due = await db.Deliveries
            .Where(d => d.Status == DeliveryStatus.Scheduled && d.DeliverAt != null && d.DeliverAt <= now)
            .ToListAsync(ct);

        if (due.Count == 0) return;

        activity?.SetTag(ActivitySources.DeliveryCountTag, due.Count);

        foreach (var delivery in due)
        {
            delivery.Status = DeliveryStatus.Active;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DeliveryActivationJob activated {Count} deliveries", due.Count);
    }
}
