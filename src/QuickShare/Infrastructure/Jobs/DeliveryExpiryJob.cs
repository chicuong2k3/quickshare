using Hangfire;
using QuickShare.Infrastructure.Telemetry;

namespace QuickShare.Infrastructure.Jobs;

public sealed class DeliveryExpiryJob(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<DeliveryExpiryJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExpireDeliveriesAsync()
    {
        var ct = CancellationToken.None;
        using var activity = ActivitySources.Source.StartActivity(ActivitySources.DeliveryExpiryActivity);
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < now && d.Status == DeliveryStatus.Active)
            .ToListAsync(ct);

        if (expired.Count == 0) return;

        activity?.SetTag(ActivitySources.DeliveryCountTag, expired.Count);

        foreach (var delivery in expired)
        {
            if (!string.IsNullOrEmpty(delivery.StoragePath))
            {
                var retries = 0;
                var maxRetries = 3;
                var delay = TimeSpan.FromSeconds(5);

                while (retries < maxRetries)
                {
                    try
                    {
                        await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
                        break;
                    }
                    catch
                    {
                        retries++;
                        if (retries < maxRetries)
                            await Task.Delay(delay, ct);
                        delay *= 2;
                    }
                }

                if (retries == maxRetries)
                    logger.LogWarning("Failed to delete R2 objects after {Retries} retries for delivery {DeliveryId}",
                        maxRetries, delivery.Id);
            }

            delivery.Status = DeliveryStatus.Expired;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DeliveryExpiryJob expired {Count} deliveries", expired.Count);
    }
}
