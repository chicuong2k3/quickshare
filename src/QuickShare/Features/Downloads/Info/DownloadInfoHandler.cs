using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Downloads.Info;

public record DownloadInfoQuery(string ShareCode) : IRequest<Result<DownloadInfoResponse>>;

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
    bool BurnAfterDownload,
    bool AllowExtensionRequests,
    bool PrivacyMode,
    bool EmailOtpEnabled
);

public sealed class DownloadInfoHandler(AppDbContext db, CacheService cache)
    : IRequestHandler<DownloadInfoQuery, Result<DownloadInfoResponse>>
{
    public async ValueTask<Result<DownloadInfoResponse>> Handle(DownloadInfoQuery query, CancellationToken cancellationToken)
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

                if (delivery is null || delivery.DeletedAt is not null)
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

                // Privacy mode (issue #187): expose the flag and zero the visible counts —
                // the recipient sees "no tracking", never a partial or misleading number.
                return Result.Success(new DownloadInfoResponse(
                    delivery.FileName,
                    delivery.ContentType,
                    delivery.FileSize,
                    delivery.ExpiresAt,
                    delivery.DeliverAt,
                    delivery.Status,
                    delivery.PrivacyMode ? 0 : delivery.DownloadCount,
                    delivery.MaxDownloads,
                    HasOtp: !string.IsNullOrEmpty(delivery.OtpHash),
                    HasPassword: !string.IsNullOrEmpty(delivery.PasswordHash),
                    SenderPlan: senderPlan,
                    Message: delivery.Message,
                    Sha256: delivery.Sha256,
                    FirstDownloadedAt: delivery.PrivacyMode ? null : delivery.FirstDownloadedAt,
                    HideDownloadStats: delivery.HideDownloadStats || delivery.PrivacyMode,
                    BurnAfterDownload: delivery.MaxDownloads == 1,
                    AllowExtensionRequests: delivery.AllowExtensionRequests,
                    PrivacyMode: delivery.PrivacyMode,
                    EmailOtpEnabled: delivery.EmailOtpEnabled
                ));
            },
            duration: TimeSpan.FromSeconds(30),
            token: cancellationToken
        );
    }
}
