using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Stats;

public record ToggleDownloadStatsCommand(Guid DeliveryId) : IRequest<Result<bool>>;

public sealed class ToggleDownloadStatsHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<ToggleDownloadStatsCommand, Result<bool>>
{
    public async ValueTask<Result<bool>> Handle(
        ToggleDownloadStatsCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<bool>(new LightResults.Error(ErrorCodes.NotFound));

        delivery.HideDownloadStats = !delivery.HideDownloadStats;
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success(delivery.HideDownloadStats);
    }
}
