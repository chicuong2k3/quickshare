using LightResults;


namespace QuickShare.Features.Deliveries;

public sealed class DeliveryService(IStorageProvider storage, IServiceScopeFactory scopeFactory, ILogger<DeliveryService> logger)
{
    public void IncrementDownloadCount(Guid deliveryId, long? egressBytes = null)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var entity = await db.Deliveries.FindAsync([deliveryId]);
                if (entity is not null)
                {
                    entity.DownloadCount++;
                    if (egressBytes.HasValue)
                        entity.EgressBytes += egressBytes.Value;

                    if (entity.DownloadCount >= entity.MaxDownloads)
                        entity.Status = "CapReached";

                    await db.SaveChangesAsync(CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to increment download count for delivery {DeliveryId}", deliveryId);
            }
        }, CancellationToken.None);
    }

    public async Task<Result> DeleteDeliveryAsync(Guid deliveryId, Guid userId, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries.FindAsync([deliveryId], ct);
        if (delivery is null)
            return Result.Failure(new NotFoundError("Delivery", deliveryId));

        if (delivery.SenderId != userId)
            return Result.Failure(new LightResults.Error("Not authorized to delete this delivery"));

        if (!string.IsNullOrEmpty(delivery.StoragePath))
        {
            try
            {
                await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete R2 objects for delivery {DeliveryId}", deliveryId);
                return Result.Failure(new LightResults.Error("Failed to clean up storage objects"));
            }
        }

        delivery.Status = "Deleted";
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted delivery {DeliveryId} with R2 cleanup", deliveryId);

        return Result.Success();
    }
}
