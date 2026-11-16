using System.Text.Json;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Folders;

namespace QuickShare.Features.Uploads.CloudImport;

public sealed class CloudImportRequest
{
    public const string ProviderGoogleDrive = "google_drive";
    public const string ProviderDropbox = "dropbox";
    public const string ProviderOneDrive = "onedrive";

    public static readonly string[] ValidProviders = [ProviderGoogleDrive, ProviderDropbox, ProviderOneDrive];

    public string Provider { get; set; } = ProviderGoogleDrive;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public string? AccessToken { get; set; }
    public long EstimatedBytes { get; set; }
    public Guid? FolderId { get; set; }
    public string? StorageRegion { get; set; }
}

public sealed record CloudImportResponse(
    Guid DeliveryId,
    string ShareCode,
    string ShareLink,
    string Provider,
    string FileName,
    long FileSize,
    DateTime? ExpiresAt);

/// <summary>
/// POST /api/uploads/cloud-import — direct cloud import from Google Drive, Dropbox, or OneDrive
/// via temporary picker token and streaming transfer without storing OAuth credentials (issue #109).
/// </summary>
public sealed class CloudImportEndpoint(
    AppDbContext db,
    IRemoteUrlFetcher fetcher,
    IStorageProvider storage,
    StorageRegionResolver regions) : Endpoint<CloudImportRequest, CloudImportResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.CloudImportApi);
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(CloudImportRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.RequiresPaidPlan, StatusCodes.Status403Forbidden);
            return;
        }

        var provider = req.Provider?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!CloudImportRequest.ValidProviders.Contains(provider))
        {
            ThrowError(ErrorCodes.CloudImportProviderInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.DownloadUrl) || string.IsNullOrWhiteSpace(req.FileName))
        {
            ThrowError(ErrorCodes.UrlInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var maxFileSize = PlanTierLimits.GetMaxFileSize(user.Plan) ?? (10L * 1024 * 1024 * 1024);
        var quota = new QuotaService(db);

        var fetchRes = await fetcher.FetchAsync(req.DownloadUrl.Trim(), maxFileSize, ct);
        if (fetchRes.Status == RemoteUrlStatus.TooLarge)
        {
            ThrowError(ErrorCodes.FileTooLarge, StatusCodes.Status400BadRequest);
            return;
        }
        if (fetchRes.Status != RemoteUrlStatus.Ok || fetchRes.Content is null)
        {
            ThrowError(ErrorCodes.UrlFetchFailed, StatusCodes.Status400BadRequest);
            return;
        }

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == user.Id && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        Guid? folderId = null;
        if (req.FolderId is { } requestedFolder && workspaceId is { } wsId)
        {
            var resolved = await FolderDefaults.ResolveAsync(db, wsId, requestedFolder, ct);
            if (resolved is null)
            {
                ThrowError(ErrorCodes.FolderNotFound, StatusCodes.Status404NotFound);
                return;
            }
            folderId = requestedFolder;
        }

        var resolvedRegion = regions.Resolve(user, user.Plan, req.StorageRegion);
        var deliveryId = Guid.CreateVersion7();
        var fileName = req.FileName.Trim();
        var contentType = !string.IsNullOrWhiteSpace(req.ContentType)
            ? req.ContentType.Trim()
            : (fetchRes.ContentType ?? "application/octet-stream");
        var objectKey = R2ObjectKey.For(user.Id, deliveryId, fileName, resolvedRegion);

        long sizeBytes;
        try
        {
            await storage.UploadObjectAsync(objectKey, fetchRes.Content, contentType, ct);
            sizeBytes = await storage.GetObjectSizeAsync(objectKey, ct);
            if (sizeBytes < 0)
                sizeBytes = req.EstimatedBytes > 0 ? req.EstimatedBytes : 4;
        }
        finally
        {
            if (fetchRes.Content is IDisposable disp)
                disp.Dispose();
        }

        var quotaResult = await quota.CheckAsync(user.Id, user.Plan, sizeBytes, ct);
        if (quotaResult.IsExceeded)
        {
            await storage.DeleteObjectAsync(objectKey, ct);
            ThrowError(ErrorCodes.MonthlyQuotaExceeded, StatusCodes.Status400BadRequest);
            return;
        }

        await quota.RecordAsync(user.Id, user.Plan, sizeBytes, ct);

        var shareCode = ShareCodes.Generate();
        var expiresAt = RetentionPolicy.Clamp(
            DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(user.Plan)),
            user.RetentionDays);

        var delivery = new Delivery
        {
            Id = deliveryId,
            SenderId = user.Id,
            WorkspaceId = workspaceId,
            FolderId = folderId,
            StorageRegion = resolvedRegion,
            FileName = fileName,
            ContentType = contentType,
            FileSize = sizeBytes,
            TotalBytes = sizeBytes,
            TotalFileBytes = sizeBytes,
            FileCount = 1,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(user.Plan),
            StoragePath = objectKey,
            ExpiresAt = expiresAt,
            Status = DeliveryStatus.Active,
            Source = DeliverySources.Cloud,
            EncryptionMode = DeliveryEncryptionMode.None,
        };

        db.Deliveries.Add(delivery);
        db.DeliveryFiles.Add(new DeliveryFile
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            FileName = delivery.FileName,
            ContentType = delivery.ContentType,
            FileSize = sizeBytes,
            ObjectKey = delivery.StoragePath!,
            SortOrder = 0,
        });

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            WorkspaceId = workspaceId,
            EventType = SecurityEventType.CloudImportCompleted,
            IpHash = string.Empty,
            UserAgentLabel = nameof(CloudImportEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { deliveryId, shareCode, provider, fileName, sizeBytes }),
        });

        await db.SaveChangesAsync(ct);

        var baseUrl = $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        await Send.OkAsync(new CloudImportResponse(
            deliveryId,
            shareCode,
            $"{baseUrl}/d/{shareCode}",
            provider,
            fileName,
            sizeBytes,
            expiresAt), ct);
    }
}
