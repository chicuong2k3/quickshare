using LightResults;
using QuickShare.Features.Uploads.Shared;


namespace QuickShare.Features.Uploads.StartR2;

public record StartR2Command(string FileName, long FileSize, string ContentType, IReadOnlyList<int> PartNumbers, string? SenderEmail, string? RequestCode)
    : IRequest<Result<StartR2Response>>;

public record StartR2Response(Guid DeliveryId, string UploadId, string ObjectKey, IReadOnlyList<R2PartUrl> Parts);

public sealed class StartR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    AnalyticsService analytics,
    ILogger<StartR2Handler> logger,
    AppDbContext db)
    : IRequestHandler<StartR2Command, Result<StartR2Response>>
{
    // S3/R2 minimum part size (all parts except the last). Caps how many presigned
    // part URLs a client can request for a declared file size.
    internal const long MinPartBytes = 5 * 1024 * 1024;

    internal const int MaxPartNumber = 10_000;

    internal static int MaxPartsFor(long fileSize)
        => (int)Math.Min(MaxPartNumber, (fileSize + MinPartBytes - 1) / MinPartBytes + 1);

    public async ValueTask<Result<StartR2Response>> Handle(StartR2Command cmd, CancellationToken cancellationToken)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, cancellationToken);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;

        Guid? fileRequestId = null;
        if (!string.IsNullOrWhiteSpace(cmd.RequestCode))
        {
            var (requestId, failure) = await ValidateRequestAsync(cmd.RequestCode, cmd.FileSize, cancellationToken);
            if (failure is not null)
                return Result.Failure<StartR2Response>(failure);
            fileRequestId = requestId;
        }

        // File-request submissions are capped by the request budget, never the submitter plan.
        if (fileRequestId is null)
        {
            var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
            if (maxFileSize.HasValue && cmd.FileSize > maxFileSize.Value)
                return Result.Failure<StartR2Response>(new LightResults.Error(
                    $"File size {cmd.FileSize} exceeds {plan} plan limit of {maxFileSize} bytes"));

            // Monthly quota check (authenticated users only; anonymous keeps the per-file cap).
            if (user is not null)
            {
                var quota = new QuotaService(db);
                var check = await quota.CheckAsync(user.Id, plan, cmd.FileSize, cancellationToken);
                if (check.IsExceeded)
                    return Result.Failure<StartR2Response>(new LightResults.Error(ErrorCodes.MonthlyQuotaExceeded));
            }
        }

        var deliveryId = Guid.CreateVersion7();

        R2InitResult initResult;
        try
        {
            initResult = await storage.InitR2MultipartUploadAsync(deliveryId, cmd.FileName, cancellationToken);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "R2 init failed for delivery {DeliveryId}", deliveryId);
            return Result.Failure<StartR2Response>(new LightResults.Error(UploadErrors.R2Upstream + "init failed"));
        }

        // Presigned URL generation is local (no network) — an exception here is a bug
        // and must surface to the global exception handler, not be masked as an R2 failure.
        var partUrls = await storage.GenerateUploadPartUrlsAsync(
            initResult.UploadId, initResult.ObjectKey, cmd.PartNumbers, cancellationToken);

        var maxExpiry = PlanTierLimits.GetMaxExpiry(plan);
        var session = new UploadSession
        {
            Id = deliveryId,
            SenderId = user?.Id,
            SenderEmail = string.IsNullOrWhiteSpace(cmd.SenderEmail) ? null : cmd.SenderEmail.Trim(),
            FileName = cmd.FileName,
            ContentType = cmd.ContentType,
            FileSize = cmd.FileSize,
            Status = UploadSessionStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.Add(maxExpiry),
            R2UploadId = initResult.UploadId,
            StoragePath = initResult.ObjectKey,
            FileRequestId = fileRequestId
        };

        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);

        await analytics.RecordAsync(AnalyticsEvents.UploadStarted,
            new AnalyticsEventData(cmd.FileName, cmd.FileSize, cmd.ContentType, plan), cancellationToken);

        return Result.Success(new StartR2Response(
            deliveryId, initResult.UploadId, initResult.ObjectKey, partUrls));
    }

    private async Task<(Guid? RequestId, LightResults.Error? Failure)> ValidateRequestAsync(
        string code, long fileSize, CancellationToken cancellationToken)
    {
        var request = await db.FileRequests
            .AsNoTracking()
            .Where(r => r.Code == code)
            .Select(r => new
            {
                r.Id,
                r.ClosedAt,
                r.ExpiresAt,
                r.MaxTotalBytes,
                r.MaxFiles,
                CurrentTotalBytes = r.Deliveries
                    .Where(d => d.Status == DeliveryStatus.Active)
                    .Sum(d => (long?)d.TotalBytes) ?? 0,
                CurrentFileCount = r.Deliveries.Count(d => d.Status == DeliveryStatus.Active),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (request is null)
            return (null, new LightResults.Error(ErrorCodes.FileRequestNotFound));

        if (request.ClosedAt is not null)
            return (null, new LightResults.Error(ErrorCodes.FileRequestClosed));

        if (request.ExpiresAt < DateTime.UtcNow)
            return (null, new LightResults.Error(ErrorCodes.FileRequestExpired));

        if (request.CurrentTotalBytes + fileSize > request.MaxTotalBytes ||
            request.CurrentFileCount >= request.MaxFiles)
            return (null, new LightResults.Error(ErrorCodes.FileRequestLimitReached));

        return (request.Id, null);
    }
}
