using QuickShare.Features.Downloads.Shared;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Downloads.Offline;

public sealed record OfflineDeliveryBundleDto(
    string ShareCode,
    string FileName,
    string ContentType,
    long FileSize,
    DateTime? ExpiresAt,
    string? Sha256,
    bool HasPassword,
    bool PrivacyMode,
    string EncryptionMode,
    string DownloadUrl,
    long MaxCacheQuotaBytes,
    DateTime SyncedAt);

/// <summary>
/// GET /api/downloads/{ShareCode}/offline-bundle — returns offline cache metadata and stream URL
/// for PWA offline reading (#279), enforcing privacy mode (#187), password gate (#44), and expiry (#22).
/// </summary>
public sealed class GetOfflineDeliveryBundleEndpoint(
    AppDbContext db,
    DeliveryAccessService access) : EndpointWithoutRequest<OfflineDeliveryBundleDto>
{
    private const long DefaultAnonymousCacheQuotaBytes = 250L * 1024 * 1024;

    public override void Configure()
    {
        Get(RoutePaths.DeliveryOfflineBundleApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode") ?? string.Empty;
        if (await access.CheckByCodeAsync(shareCode, ct) != DeliveryAccessDecision.Allowed)
        {
            ThrowError(ErrorCodes.DeliveryRestricted, StatusCodes.Status403Forbidden);
            return;
        }

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null || delivery.Status == DeliveryStatus.PendingApproval)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (delivery.Status != DeliveryStatus.Active ||
            (delivery.ExpiresAt.HasValue && delivery.ExpiresAt.Value <= DateTime.UtcNow))
        {
            ThrowError(ErrorCodes.DeliveryExpired, StatusCodes.Status410Gone);
            return;
        }

        // Privacy mode (#187) never caches sensitive deliveries onto a device's persistent offline store.
        if (delivery.PrivacyMode)
        {
            ThrowError(ErrorCodes.OfflineCacheForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var maxCacheBytes = PlanTierLimits.GetMaxFileSize(plan) ?? DefaultAnonymousCacheQuotaBytes;

        HttpContext.Response.Headers.CacheControl = "private, max-age=3600";
        await Send.OkAsync(new OfflineDeliveryBundleDto(
            delivery.ShareCode,
            delivery.FileName,
            delivery.ContentType,
            delivery.FileSize,
            delivery.ExpiresAt,
            delivery.Sha256,
            !string.IsNullOrEmpty(delivery.PasswordHash),
            delivery.PrivacyMode,
            string.IsNullOrEmpty(delivery.EncryptionMode) ? DeliveryEncryptionMode.FragmentKey : delivery.EncryptionMode,
            $"/api/downloads/{delivery.ShareCode}",
            maxCacheBytes,
            DateTime.UtcNow), ct);
    }
}

public sealed class OfflineQueueCheckItem
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
}

public sealed class OfflineQueueCheckRequest
{
    public List<OfflineQueueCheckItem> Items { get; set; } = [];
}

public sealed record OfflineQueueItemVerdictDto(
    string FileName,
    long FileSize,
    bool Allowed,
    string? ErrorCode);

public sealed record OfflineQueueCheckResponse(
    string Plan,
    long? MaxSingleFileSizeBytes,
    int AllowedCount,
    int RejectedCount,
    IReadOnlyList<OfflineQueueItemVerdictDto> Items);

/// <summary>
/// POST /api/uploads/offline-queue — preflight and reconnect validation for PWA offline upload queue (#80).
/// Checks PlanTierLimits and monthly quota before/during background sync.
/// </summary>
public sealed class OfflineUploadQueueCheckEndpoint(
    AppDbContext db) : Endpoint<OfflineQueueCheckRequest, OfflineQueueCheckResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.OfflineUploadQueueApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(OfflineQueueCheckRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        var quota = user is null ? null : new QuotaService(db);

        var items = req.Items ?? [];
        var verdicts = new List<OfflineQueueItemVerdictDto>(items.Count);
        var runningBytes = 0L;
        var allowedCount = 0;
        var rejectedCount = 0;

        foreach (var item in items)
        {
            if (item.FileSize <= 0 || string.IsNullOrWhiteSpace(item.FileName))
            {
                rejectedCount++;
                verdicts.Add(new OfflineQueueItemVerdictDto(item.FileName, item.FileSize, false, ErrorCodes.FileNameInvalid));
                continue;
            }

            if (maxFileSize.HasValue && item.FileSize > maxFileSize.Value)
            {
                rejectedCount++;
                verdicts.Add(new OfflineQueueItemVerdictDto(item.FileName, item.FileSize, false, ErrorCodes.FileTooLarge));
                continue;
            }

            if (quota is not null && user is not null)
            {
                var qCheck = await quota.CheckAsync(user.Id, plan, runningBytes + item.FileSize, ct);
                if (qCheck.IsExceeded)
                {
                    rejectedCount++;
                    verdicts.Add(new OfflineQueueItemVerdictDto(item.FileName, item.FileSize, false, ErrorCodes.MonthlyQuotaExceeded));
                    continue;
                }
            }

            runningBytes += item.FileSize;
            allowedCount++;
            verdicts.Add(new OfflineQueueItemVerdictDto(item.FileName, item.FileSize, true, null));
        }

        await Send.OkAsync(new OfflineQueueCheckResponse(
            plan,
            maxFileSize,
            allowedCount,
            rejectedCount,
            verdicts), ct);
    }
}
