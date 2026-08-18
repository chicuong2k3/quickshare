using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Downloads.PublicStats;

public record PublicStatsQuery(string ShareCode) : IRequest<Result<PublicStatsResponse>>;

public record DailyStat(string Day, int Count);

public record PublicStatsResponse(
    string FileName,
    long FileSize,
    int TotalDownloads,
    int UniqueVisitors,
    DateTime? FirstDownloadedAt,
    IReadOnlyList<DailyStat> Timeline
);

/// <summary>
/// Aggregated download stats for a delivery (issue #294). Serves only counts and a
/// 7-day timeline — never per-recipient identity. Opt-in via Delivery.PublicStatsEnabled,
/// and privacy-mode deliveries never expose a stats page.
/// </summary>
public sealed class PublicStatsHandler(AppDbContext db, CacheService cache)
    : IRequestHandler<PublicStatsQuery, Result<PublicStatsResponse>>
{
    private const int TimelineDays = 7;

    public async ValueTask<Result<PublicStatsResponse>> Handle(PublicStatsQuery query, CancellationToken cancellationToken)
    {
        return await cache.GetOrSetAsync(
            $"public-stats:{query.ShareCode}",
            async ct2 =>
            {
                var delivery = await db.Deliveries
                    .AsNoTracking()
                    .ByCode(query.ShareCode)
                    .FirstOrDefaultAsync(ct2);

                if (delivery is null ||
                    delivery.DeletedAt is not null ||
                    !delivery.PublicStatsEnabled ||
                    delivery.PrivacyMode)
                    return Result.Failure<PublicStatsResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

                if (delivery.Status == DeliveryStatus.Suspended)
                    return Result.Failure<PublicStatsResponse>(new LightResults.Error(ErrorCodes.DeliverySuspended));

                if (delivery.Status == DeliveryStatus.Revoked)
                    return Result.Failure<PublicStatsResponse>(new LightResults.Error(ErrorCodes.DeliveryRevoked));

                if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
                    return Result.Failure<PublicStatsResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

                var trackings = await db.DownloadTrackings
                    .AsNoTracking()
                    .Where(t => t.DeliveryId == delivery.Id)
                    .Select(t => new { t.DownloadedAt, t.IpAddress })
                    .ToListAsync(ct2);

                var today = DateTime.UtcNow.Date;
                var timeline = Enumerable.Range(0, TimelineDays)
                    .Select(offset => today.AddDays(-(TimelineDays - 1 - offset)))
                    .Select(day => new DailyStat(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), trackings.Count(t => t.DownloadedAt.Date == day)))
                    .ToList();

                return Result.Success(new PublicStatsResponse(
                    delivery.FileName,
                    delivery.FileSize,
                    trackings.Count,
                    trackings.Select(t => t.IpAddress).Distinct().Count(),
                    delivery.FirstDownloadedAt,
                    timeline));
            },
            duration: TimeSpan.FromSeconds(30),
            token: cancellationToken
        );
    }
}
