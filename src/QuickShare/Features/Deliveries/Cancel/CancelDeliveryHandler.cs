using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Cancel;

public record CancelDeliveryCommand(Guid DeliveryId) : IRequest<Result>;

public sealed class CancelDeliveryHandler(
    IStorageProvider storage,
    ILogger<CancelDeliveryHandler> logger,
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache)
    : IRequestHandler<CancelDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(CancelDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status != DeliveryStatus.Scheduled)
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryNotScheduled));

        if (!string.IsNullOrEmpty(delivery.StoragePath))
        {
            try
            {
                await storage.DeleteObjectsAsync(delivery.StoragePath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete R2 objects for delivery {DeliveryId}", delivery.Id);
            }
        }

        db.Deliveries.Remove(delivery);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}
