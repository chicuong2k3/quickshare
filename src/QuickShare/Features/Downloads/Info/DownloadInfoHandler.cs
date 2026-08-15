using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Downloads.Info;

public record DownloadInfoQuery(string ShareCode);

public record DownloadInfoResponse(
    string FileName,
    string ContentType,
    long FileSize,
    DateTime? ExpiresAt,
    DateTime? DeliverAt,
    string Status,
    int DownloadCount,
    int MaxDownloads,
    bool HasOtp,
    string SenderPlan
);

public sealed class DownloadInfoHandler
{
    public async Task<Result<DownloadInfoResponse>> Handle(DownloadInfoQuery query, AppDbContext db, CacheService cache, CancellationToken ct)
    {
        return await cache.GetOrSetAsync(
            $"download:info:{query.ShareCode}",
            async ct2 =>
            {
                var delivery = await db.Deliveries
                    .AsNoTracking()
                    .Include(d => d.Sender)
                    .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct2);

                if (delivery is null)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

                if (delivery.Status == DeliveryStatus.Suspended)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliverySuspended));

                if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

                if (delivery.DownloadCount >= delivery.MaxDownloads)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DownloadLimitReached));

                var senderPlan = string.IsNullOrEmpty(delivery.Sender?.Plan) ? PlanTierLimits.Anonymous : delivery.Sender.Plan;

                return Result.Success(new DownloadInfoResponse(
                    delivery.FileName,
                    delivery.ContentType,
                    delivery.FileSize,
                    delivery.ExpiresAt,
                    delivery.DeliverAt,
                    delivery.Status,
                    delivery.DownloadCount,
                    delivery.MaxDownloads,
                    HasOtp: !string.IsNullOrEmpty(delivery.OtpHash),
                    SenderPlan: senderPlan
                ));
            },
            duration: TimeSpan.FromSeconds(30),
            token: ct
        );
    }
}
