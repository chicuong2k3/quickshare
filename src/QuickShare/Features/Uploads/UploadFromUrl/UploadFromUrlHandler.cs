using LightResults;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Features.Uploads.UploadFromUrl;

public record UploadFromUrlCommand(string Url) : IRequest<Result<UploadFromUrlResponse>>;

public record UploadFromUrlResponse(string ShareCode, string ShareLink, DateTime ExpiresAt);

public sealed class UploadFromUrlHandler(
    IRemoteUrlFetcher fetcher,
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    AppDbContext db,
    AnalyticsService analytics,
    ILogger<UploadFromUrlHandler> logger)
    : IRequestHandler<UploadFromUrlCommand, Result<UploadFromUrlResponse>>
{
    public async ValueTask<Result<UploadFromUrlResponse>> Handle(UploadFromUrlCommand cmd, CancellationToken cancellationToken)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, cancellationToken);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;

        // Remote fetch is a paid convenience — free/anonymous users upload directly.
        if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        // A Pro/Studio plan implies a signed-in user (anonymous has no plan above Free).
        var owner = user!;

        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        var cap = maxFileSize ?? 0L;
        if (cap <= 0)
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var result = await fetcher.FetchAsync(cmd.Url, cap, cancellationToken);

        if (result.Status == RemoteUrlStatus.InvalidUrl)
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlInvalid));
        if (result.Status == RemoteUrlStatus.TooLarge)
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlTooLarge));
        if (result.Status != RemoteUrlStatus.Ok || result.Content is null)
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlFetchFailed));

        var deliveryId = Guid.CreateVersion7();
        var objectKey = R2ObjectKey.For(owner.Id, deliveryId, result.FileName ?? "download");

        long actualSize;
        try
        {
            await storage.UploadObjectAsync(objectKey, result.Content, result.ContentType ?? "application/octet-stream", cancellationToken);
        }
        catch (RemoteUrlTooLargeException)
        {
            await TryDeleteAsync(objectKey, cancellationToken);
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlTooLarge));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Upload-from-URL failed for {Url}", cmd.Url);
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlFetchFailed));
        }
        finally
        {
            if (result.Content is IDisposable d) d.Dispose();
        }

        actualSize = await storage.GetObjectSizeAsync(objectKey, cancellationToken);
        if (actualSize < 0) actualSize = 0;
        if (actualSize > cap)
        {
            await TryDeleteAsync(objectKey, cancellationToken);
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.UrlTooLarge));
        }

        var quota = new QuotaService(db);
        var quotaCheck = await quota.CheckAsync(owner.Id, plan, actualSize, cancellationToken);
        if (quotaCheck.IsExceeded)
        {
            await TryDeleteAsync(objectKey, cancellationToken);
            return Result.Failure<UploadFromUrlResponse>(new LightResults.Error(ErrorCodes.MonthlyQuotaExceeded));
        }

        var shareCode = ShareCodes.Generate();
        var expiresAt = DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(plan));

        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = owner.Id,
            FileName = result.FileName ?? "download",
            ContentType = result.ContentType ?? "application/octet-stream",
            FileSize = actualSize,
            TotalBytes = actualSize,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = objectKey,
            ExpiresAt = expiresAt,
            Status = DeliveryStatus.Active,
            Source = DeliverySources.Url,
        };

        db.Deliveries.Add(delivery);
        await quota.RecordAsync(owner.Id, plan, actualSize, cancellationToken);
        await ReferralService.ReleaseOnFirstUploadAsync(owner, db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await analytics.RecordAsync(AnalyticsEvents.UploadFromUrl,
            new AnalyticsEventData(delivery.FileName, delivery.FileSize, delivery.ContentType, plan), cancellationToken);

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        var shareLink = $"{baseUrl}{RoutePaths.DeliveryPage}{shareCode}";
        if (!string.IsNullOrEmpty(owner.ReferralCode))
            shareLink += $"?{ReferralPolicy.QueryParam}={owner.ReferralCode}";

        return Result.Success(new UploadFromUrlResponse(shareCode, shareLink, expiresAt));
    }

    private async Task TryDeleteAsync(string objectKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteObjectAsync(objectKey, cancellationToken);
        }
        catch
        {
            // best-effort cleanup; the orphan sweeper handles leftovers
        }
    }
}
