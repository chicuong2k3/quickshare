using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Cancel;

public record CancelDeliveryCommand(Guid DeliveryId);

public sealed class CancelDeliveryHandler(
    IStorageProvider storage,
    ILogger<CancelDeliveryHandler> logger)
{
    public async Task<Result> Handle(CancelDeliveryCommand cmd, AppDbContext db, IHttpContextAccessor http, CacheService cache, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status != DeliveryStatus.Scheduled)
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryNotScheduled));

        if (!string.IsNullOrEmpty(delivery.StoragePath))
        {
            try
            {
                await storage.DeleteObjectsAsync(delivery.StoragePath, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete R2 objects for delivery {DeliveryId}", delivery.Id);
            }
        }

        db.Deliveries.Remove(delivery);
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success();
    }
}
