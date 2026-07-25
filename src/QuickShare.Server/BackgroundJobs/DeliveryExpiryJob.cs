using QuickShare.Server.Abstractions;

namespace QuickShare.Server.BackgroundJobs;

public sealed class DeliveryExpiryJob(IServiceScopeFactory scopeFactory, IStorageProvider storage, ILogger<DeliveryExpiryJob> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, stoppingToken);
                await ExpireDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { logger.LogError(ex, "DeliveryExpiryJob cycle failed"); }
        }
    }

    private async Task ExpireDeliveriesAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var expired = await db.Deliveries
            .Where(d => d.ExpiresAt != null && d.ExpiresAt < now && d.Status == "Active")
            .ToListAsync(ct);

        if (expired.Count == 0) return;

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

            delivery.Status = "Expired";
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("DeliveryExpiryJob expired {Count} deliveries", expired.Count);
    }
}
