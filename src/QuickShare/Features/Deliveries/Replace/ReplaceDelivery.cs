using LightResults;
using QuickShare.Features.Uploads.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Deliveries.Replace;

public record ReplaceDeliveryStartCommand(Guid DeliveryId, string FileName, long FileSize, string ContentType, IReadOnlyList<int> PartNumbers)
    : IRequest<Result<ReplaceDeliveryStartResponse>>;

public record ReplaceDeliveryStartResponse(Guid SessionId, string UploadId, string ObjectKey, IReadOnlyList<R2PartUrl> Parts);

public record ReplaceDeliveryCommitCommand(Guid DeliveryId, Guid SessionId, string UploadId, string ObjectKey, IReadOnlyList<R2PartEtag> Parts, string? Sha256)
    : IRequest<Result>;

/// <summary>
/// "Replace file, keep link" (issue #72): re-uploads a file under an existing
/// delivery so the share link, password, expiry, branding and download cap all
/// stay unchanged; only the bytes (and size/hash/download-count) change.
/// </summary>
public sealed class ReplaceDeliveryStartHandler(IStorageProvider storage, IHttpContextAccessor http, AppDbContext db, AnalyticsService analytics)
    : IRequestHandler<ReplaceDeliveryStartCommand, Result<ReplaceDeliveryStartResponse>>
{
    public async ValueTask<Result<ReplaceDeliveryStartResponse>> Handle(ReplaceDeliveryStartCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var plan = user.Plan;

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<ReplaceDeliveryStartResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked or DeliveryStatus.Scheduled)
            return Result.Failure<ReplaceDeliveryStartResponse>(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        // File lock/checkout (issue #308): another user holds the lock — block replacement.
        var now = DateTime.UtcNow;
        var lockedByOther = await db.FileLocks
            .AnyAsync(l => l.DeliveryId == delivery.Id
                && l.ReleasedAt == null
                && l.ExpiresAt > now
                && l.LockedById != user.Id, cancellationToken);

        if (lockedByOther)
        {
            await analytics.RecordAsync(AnalyticsEvents.ReplaceBlocked,
                new AnalyticsEventData(FileName: delivery.FileName), cancellationToken);
            return Result.Failure<ReplaceDeliveryStartResponse>(new LightResults.Error(ErrorCodes.DeliveryLocked));
        }

        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        if (maxFileSize.HasValue && cmd.FileSize > maxFileSize.Value)
            return Result.Failure<ReplaceDeliveryStartResponse>(new LightResults.Error(
                $"File size {cmd.FileSize} exceeds {plan} plan limit of {maxFileSize} bytes"));

        var sessionId = Guid.CreateVersion7();

        R2InitResult initResult;
        try
        {
            // Same region as the delivery being replaced: swapping a file must not silently
            // move the bytes to another jurisdiction (issue #119).
            initResult = await storage.InitR2MultipartUploadAsync(
                sessionId, cmd.FileName, delivery.StorageRegion, cancellationToken);
        }
        catch (R2StorageException)
        {
            return Result.Failure<ReplaceDeliveryStartResponse>(new LightResults.Error(UploadErrors.R2Upstream + "init failed"));
        }

        var partUrls = await storage.GenerateUploadPartUrlsAsync(
            initResult.UploadId, initResult.ObjectKey, cmd.PartNumbers, cancellationToken);

        var session = new UploadSession
        {
            Id = sessionId,
            SenderId = user.Id,
            FileName = cmd.FileName,
            ContentType = cmd.ContentType,
            FileSize = cmd.FileSize,
            Status = UploadSessionStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            R2UploadId = initResult.UploadId,
            StoragePath = initResult.ObjectKey,
            ReplaceDeliveryId = delivery.Id
        };

        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ReplaceDeliveryStartResponse(
            sessionId, initResult.UploadId, initResult.ObjectKey, partUrls));
    }
}

public sealed class ReplaceDeliveryCommitHandler(IStorageProvider storage, ILogger<ReplaceDeliveryCommitHandler> logger, AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<ReplaceDeliveryCommitCommand, Result>
{
    public async ValueTask<Result> Handle(ReplaceDeliveryCommitCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        var session = await db.UploadSessions.FirstOrDefaultAsync(
            s => s.Id == cmd.SessionId
                && s.ReplaceDeliveryId == cmd.DeliveryId
                && s.Status == UploadSessionStatus.InProgress
                && s.R2UploadId == cmd.UploadId
                && s.StoragePath == cmd.ObjectKey, cancellationToken);

        if (session is null)
            return Result.Failure(new LightResults.Error("No matching in-progress replace session"));

        var sha256 = cmd.Sha256?.Trim().ToLowerInvariant();
        if (sha256 is { Length: > 0 } && (sha256.Length != 64 || sha256.Any(c => !char.IsAsciiHexDigit(c))))
            return Result.Failure(new LightResults.Error(ErrorCodes.Sha256Invalid));

        CompletionResult completion;
        try
        {
            completion = await storage.CompleteR2MultipartUploadAsync(cmd.UploadId, cmd.ObjectKey, cmd.Parts, cancellationToken);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "Replace commit failed for delivery {DeliveryId}", cmd.DeliveryId);
            return Result.Failure(new LightResults.Error(UploadErrors.R2Upstream + "commit failed"));
        }

        if (!completion.Success)
            return Result.Failure(new LightResults.Error(completion.ErrorMessage ?? "R2 commit returned failure"));

        // ── Version snapshot (issue #110) ──
        // Pro/Studio: preserve the previous bytes as a version before overwriting.
        // Free: keep the old overwrite-and-delete behavior.
        var plan = user.Plan;
        var canVersion = plan is PlanTierLimits.Pro or PlanTierLimits.Studio;
        var hasOldObject = !string.IsNullOrEmpty(delivery.StoragePath) && delivery.StoragePath != cmd.ObjectKey;

        if (canVersion && hasOldObject)
        {
            var existingVersions = await db.DeliveryVersions
                .Where(v => v.DeliveryId == delivery.Id)
                .OrderBy(v => v.VersionNumber)
                .ToListAsync(cancellationToken);

            var maxVersions = plan == PlanTierLimits.Studio ? int.MaxValue : DeliveryVersion.MaxVersionsPro;

            // If at the limit, drop the oldest version and delete its object.
            if (existingVersions.Count >= maxVersions)
            {
                var oldest = existingVersions.First();
                try { await storage.DeleteObjectsAsync(oldest.StoragePath, cancellationToken); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to delete old version object for delivery {Id}", delivery.Id); }
                db.DeliveryVersions.Remove(oldest);
            }

            var nextNumber = existingVersions.Count > 0
                ? existingVersions.Max(v => v.VersionNumber) + 1
                : 1;

            db.DeliveryVersions.Add(new DeliveryVersion
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery.Id,
                VersionNumber = nextNumber,
                FileName = delivery.FileName,
                ContentType = delivery.ContentType,
                FileSize = delivery.FileSize,
                StoragePath = delivery.StoragePath,
                Sha256 = delivery.Sha256,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else if (!canVersion && hasOldObject)
        {
            // Free plan: delete the old object as before.
            try { await storage.DeleteObjectsAsync(delivery.StoragePath, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to delete old object for replaced delivery {DeliveryId}", delivery.Id); }
        }

        // FileSize/ContentType/ChunkCount/TotalBytes are init-only on Delivery — update
        // them via ExecuteUpdateAsync (EF property accessors, not C# init restrictions).
        await db.Deliveries
            .Where(d => d.Id == cmd.DeliveryId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(d => d.StoragePath, cmd.ObjectKey)
                .SetProperty(d => d.FileName, session.FileName)
                .SetProperty(d => d.ContentType, session.ContentType)
                .SetProperty(d => d.FileSize, session.FileSize)
                .SetProperty(d => d.TotalBytes, session.FileSize)
                .SetProperty(d => d.ChunkCount, cmd.Parts.Count)
                .SetProperty(d => d.Sha256, string.IsNullOrEmpty(sha256) ? null : sha256)
                .SetProperty(d => d.DownloadCount, 0)
                .SetProperty(d => d.FirstDownloadedAt, (DateTime?)null), cancellationToken);

        session.Status = UploadSessionStatus.Committed;
        session.R2UploadId = null;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}

public sealed class ReplaceDeliveryStartEndpoint : Endpoint<ReplaceDeliveryStartRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/replace/start");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Start replacing a delivery's file (keeps the link)";
            s.Description = "Sender only (issue #72).";
        });
    }

    public override async Task HandleAsync(ReplaceDeliveryStartRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<Result<ReplaceDeliveryStartResponse>>(
            new ReplaceDeliveryStartCommand(id, req.FileName, req.FileSize, req.ContentType, req.PartNumbers ?? []), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                ErrorCodes.DeliveryNotActive => 400,
                ErrorCodes.DeliveryLocked => 423,
                _ => 400
            });
    }
}

public sealed class ReplaceDeliveryCommitEndpoint : Endpoint<ReplaceDeliveryCommitRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/replace/commit");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Finish replacing a delivery's file (keeps the link)";
            s.Description = "Sender only (issue #72).";
        });
    }

    public override async Task HandleAsync(ReplaceDeliveryCommitRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new ReplaceDeliveryCommitCommand(id, req.SessionId, req.UploadId, req.ObjectKey, req.Parts ?? [], req.Sha256), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 400);
    }
}

public class ReplaceDeliveryStartRequest
{
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public IReadOnlyList<int>? PartNumbers { get; set; }
}

public class ReplaceDeliveryCommitRequest
{
    public Guid SessionId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public string ObjectKey { get; set; } = string.Empty;
    public IReadOnlyList<R2PartEtag>? Parts { get; set; }
    public string? Sha256 { get; set; }
}
