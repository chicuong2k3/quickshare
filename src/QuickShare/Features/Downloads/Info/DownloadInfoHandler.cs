using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Downloads.Info;

public record DownloadInfoQuery(string ShareCode) : IRequest<Result<DownloadInfoResponse>>;

/// <summary>A previous file version available for download (issue #110).</summary>
public record VersionInfo(int VersionNumber, string FileName, long FileSize, DateTime ReplacedAt);

public record DownloadInfoResponse(
    string FileName,
    string ContentType,
    long FileSize,
    DateTime? ExpiresAt,
    DateTime? DeliverAt,
    string Status,
    int DownloadCount,
    int MaxDownloads,
    bool HasPassword,
    string SenderPlan,
    string? Message,
    string? Sha256,
    DateTime? FirstDownloadedAt,
    bool HideDownloadStats,
    bool BurnAfterDownload,
    bool AllowExtensionRequests,
    bool PrivacyMode,
    bool EmailOtpEnabled,
    bool RequireRecipientEmail,
    IReadOnlyList<VersionInfo>? Versions = null,
    bool VerifiedSender = false,
    string? SenderDisplayName = null,
    bool IsQueued = false,
    int QueuePosition = 0,
    int QueueEtaSeconds = 0,
    bool SuppressAdInterstitial = false,
    bool IsLiveStreaming = false,
    int LiveProgressPercent = 100
);

public sealed class DownloadInfoHandler(
    AppDbContext db,
    CacheService cache,
    IOptions<QuickShareOptions> quickShareOptions)
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
                {
                    var liveSession = await db.UploadSessions
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.ShareCode == query.ShareCode && s.Status == UploadSessionStatus.InProgress && s.ExpiresAt > DateTime.UtcNow, ct2);
                    if (liveSession is not null)
                    {
                        var totalParts = Math.Max(1, liveSession.TotalPartsCount);
                        var pct = Math.Clamp((int)Math.Round(liveSession.UploadedPartsCount * 100.0 / totalParts), 0, 99);
                        return Result.Success(new DownloadInfoResponse(
                            liveSession.FileName,
                            liveSession.ContentType,
                            liveSession.FileSize,
                            liveSession.ExpiresAt,
                            null,
                            DeliveryStatus.Active,
                            0,
                            100,
                            HasPassword: false,
                            SenderPlan: PlanTierLimits.Anonymous,
                            Message: null,
                            Sha256: null,
                            FirstDownloadedAt: null,
                            HideDownloadStats: false,
                            BurnAfterDownload: false,
                            AllowExtensionRequests: false,
                            PrivacyMode: false,
                            EmailOtpEnabled: false,
                            RequireRecipientEmail: false,
                            IsLiveStreaming: true,
                            LiveProgressPercent: pct));
                    }

                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));
                }

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
                var isVerifiedSender = !delivery.PrivacyMode
                    && delivery.Sender is not null
                    && senderPlan is PlanTierLimits.Pro or PlanTierLimits.Studio;
                var senderDisplayName = isVerifiedSender && !string.IsNullOrWhiteSpace(delivery.Sender?.Name)
                    ? delivery.Sender.Name
                    : null;

                // Version history (issue #110): include past versions so the download page can render a picker.
                var versions = await db.DeliveryVersions
                    .AsNoTracking()
                    .Where(v => v.DeliveryId == delivery.Id)
                    .OrderByDescending(v => v.VersionNumber)
                    .Select(v => new VersionInfo(v.VersionNumber, v.FileName, v.FileSize, v.CreatedAt))
                    .ToListAsync(ct2);

                var effectiveBytes = delivery.TotalFileBytes > 0 ? delivery.TotalFileBytes : delivery.FileSize;
                var queue = BoardingQueuePolicy.Evaluate(senderPlan, effectiveBytes, false, quickShareOptions.Value);

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
                    HasPassword: !string.IsNullOrEmpty(delivery.PasswordHash),
                    SenderPlan: senderPlan,
                    Message: delivery.Message,
                    Sha256: delivery.Sha256,
                    FirstDownloadedAt: delivery.PrivacyMode ? null : delivery.FirstDownloadedAt,
                    HideDownloadStats: delivery.HideDownloadStats || delivery.PrivacyMode,
                    BurnAfterDownload: delivery.MaxDownloads == 1,
                    AllowExtensionRequests: delivery.AllowExtensionRequests,
                    PrivacyMode: delivery.PrivacyMode,
                    EmailOtpEnabled: delivery.EmailOtpEnabled,
                    RequireRecipientEmail: delivery.RequireRecipientEmail,
                    Versions: versions.Count > 0 ? versions : null,
                    VerifiedSender: isVerifiedSender,
                    SenderDisplayName: senderDisplayName,
                    IsQueued: queue.IsQueued,
                    QueuePosition: queue.Position,
                    QueueEtaSeconds: queue.EtaSeconds,
                    SuppressAdInterstitial: queue.SuppressAdInterstitial,
                    IsLiveStreaming: false,
                    LiveProgressPercent: 100
                ));
            },
            duration: TimeSpan.FromSeconds(30),
            token: cancellationToken
        );
    }
}
