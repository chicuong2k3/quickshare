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
    bool HasPassword,
    string SenderPlan,
    string? Message,
    string? Sha256,
    DateTime? FirstDownloadedAt,
    bool HideDownloadStats,
    bool BurnAfterDownload
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
                    .ByCode(query.ShareCode)
                    .FirstOrDefaultAsync(ct2);

                if (delivery is null)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

                if (delivery.Status == DeliveryStatus.Suspended)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliverySuspended));

                if (delivery.Status == DeliveryStatus.Revoked)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryRevoked));

                if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

                if (delivery.DownloadCount >= delivery.MaxDownloads)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DownloadLimitReached));

                // Daily cap (issue #227) — the page shows "come back tomorrow" instead of a dead button.
                if (delivery.DailyDownloadLimit is { } dailyLimit)
                {
                    var todayStart = DateTime.UtcNow.Date;
                    var todayCount = await db.DownloadTrackings
                        .AsNoTracking()
                        .CountAsync(t => t.DeliveryId == delivery.Id && t.DownloadedAt >= todayStart, ct2);
                    if (todayCount >= dailyLimit)
                        return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryDailyLimitReached));
                }

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
                    HasPassword: !string.IsNullOrEmpty(delivery.PasswordHash),
                    SenderPlan: senderPlan,
                    Message: delivery.Message,
                    Sha256: delivery.Sha256,
                    FirstDownloadedAt: delivery.FirstDownloadedAt,
                    HideDownloadStats: delivery.HideDownloadStats,
                    BurnAfterDownload: delivery.MaxDownloads == 1
                ));
            },
            duration: TimeSpan.FromSeconds(30),
            token: ct
        );
    }
}
