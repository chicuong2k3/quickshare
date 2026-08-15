using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Stats;

public record ToggleDownloadStatsCommand(Guid DeliveryId);

public sealed class ToggleDownloadStatsHandler
{
    public async Task<Result<bool>> Handle(
        ToggleDownloadStatsCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CacheService cache,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure<bool>(new LightResults.Error(ErrorCodes.NotFound));

        delivery.HideDownloadStats = !delivery.HideDownloadStats;
        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success(delivery.HideDownloadStats);
    }
}
