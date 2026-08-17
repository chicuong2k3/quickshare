using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Revoke;

public record RevokeDeliveryCommand(Guid DeliveryId) : IRequest<Result>;

/// <summary>
/// Kills a live link before its expiry (issue #322). The row stays so the sender's
/// list still shows what happened; the stored bytes are deleted immediately.
/// </summary>
public sealed class RevokeDeliveryHandler(
    IStorageProvider storage,
    ILogger<RevokeDeliveryHandler> logger,
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache)
    : IRequestHandler<RevokeDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(RevokeDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status is not (DeliveryStatus.Active or DeliveryStatus.Scheduled))
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        if (!string.IsNullOrEmpty(delivery.StoragePath))
        {
            try
            {
                await storage.DeleteObjectsAsync(delivery.StoragePath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete R2 objects for revoked delivery {DeliveryId}", delivery.Id);
            }
        }

        delivery.Status = DeliveryStatus.Revoked;
        delivery.StoragePath = null;
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}
