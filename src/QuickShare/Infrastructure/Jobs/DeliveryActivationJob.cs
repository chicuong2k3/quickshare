using QuickShare.Infrastructure.Telemetry;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryActivationJob(IServiceScopeFactory scopeFactory, ILogger<DeliveryActivationJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await ActivateDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "DeliveryActivationJob cycle failed"); }
        }
    }

    internal async Task ActivateDeliveriesAsync(CancellationToken ct)
    {
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
