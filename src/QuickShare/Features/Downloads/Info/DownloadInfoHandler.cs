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
    bool EmailOtpEnabled,
    bool RequireRecipientEmail,
    IReadOnlyList<VersionInfo>? Versions = null,
    bool VerifiedSender = false,
    string? SenderDisplayName = null,
    string VirusScanStatus = DeliveryVirusScanStatus.NotApplicable,
    string? VirusSignature = null,
    string StorageTier = DeliveryStorageTier.Hot,
    bool IsQueued = false,
    int QueuePosition = 0,
    int QueueEtaSeconds = 0,
    bool SuppressAdInterstitial = false,
    string EncryptionMode = DeliveryEncryptionMode.FragmentKey,
    string? EncryptionKdfSalt = null,
    int? EncryptionKdfIterations = null,
    bool HasKeyEscrow = false
);

public sealed class DownloadInfoHandler(
    AppDbContext db,
    CacheService cache,
    DeliveryAccessService access,
    IOptions<QuickShareOptions> quickShareOptions)
    : IRequestHandler<DownloadInfoQuery, Result<DownloadInfoResponse>>
{
    public async ValueTask<Result<DownloadInfoResponse>> Handle(DownloadInfoQuery query, CancellationToken cancellationToken)
    {
        // Scope rules (issues #142/#166) are evaluated per caller, so they run OUTSIDE
        // the share-code cache below — a verdict for one visitor must never be reused.
        if (await access.CheckByCodeAsync(query.ShareCode, cancellationToken) != DeliveryAccessDecision.Allowed)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryRestricted));

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

                // Held for review (issue #162). Reported as not-found rather than as a distinct
                // state: a recipient who was sent the link early should learn nothing about an
                // internal approval process, and "this file is awaiting sign-off" is a fact
                // about the sender's organisation, not about the recipient.
                if (delivery.Status == DeliveryStatus.PendingApproval)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

                if (delivery.VirusScanStatus == DeliveryVirusScanStatus.Infected)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryInfected));

                if (delivery.Status == DeliveryStatus.Suspended)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliverySuspended));

                if (delivery.Status == DeliveryStatus.Revoked)
                    return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryRevoked));

                if (!string.IsNullOrEmpty(delivery.ByokKeyId) && delivery.WorkspaceId is { } byokWsId)
                {
                    var byokStatus = await db.WorkspaceByokConfigs
                        .AsNoTracking()
                        .Where(x => x.WorkspaceId == byokWsId)
                        .Select(x => x.Status)
                        .FirstOrDefaultAsync(ct2);
                    if (byokStatus == WorkspaceByokConfig.StatusRevoked)
                        return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.ByokKeyRevoked));
                }

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
                var storageTier = string.IsNullOrEmpty(delivery.StorageTier) ? DeliveryStorageTier.Hot : delivery.StorageTier;
                var scanStatus = string.IsNullOrEmpty(delivery.VirusScanStatus) ? DeliveryVirusScanStatus.NotApplicable : delivery.VirusScanStatus;

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
                    EmailOtpEnabled: delivery.EmailOtpEnabled,
                    RequireRecipientEmail: delivery.RequireRecipientEmail,
                    Versions: versions.Count > 0 ? versions : null,
                    VerifiedSender: isVerifiedSender,
                    SenderDisplayName: senderDisplayName,
                    VirusScanStatus: scanStatus,
                    VirusSignature: delivery.VirusSignature,
                    StorageTier: storageTier,
                    IsQueued: queue.IsQueued,
                    QueuePosition: queue.Position,
                    QueueEtaSeconds: queue.EtaSeconds,
                    SuppressAdInterstitial: queue.SuppressAdInterstitial,
                    EncryptionMode: string.IsNullOrEmpty(delivery.EncryptionMode) ? DeliveryEncryptionMode.FragmentKey : delivery.EncryptionMode,
                    EncryptionKdfSalt: delivery.EncryptionKdfSalt,
                    EncryptionKdfIterations: delivery.EncryptionKdfIterations,
                    HasKeyEscrow: !string.IsNullOrEmpty(delivery.EncryptedKeyEscrow)
                ));
            },
            duration: TimeSpan.FromSeconds(30),
            token: cancellationToken
        );
    }
}
