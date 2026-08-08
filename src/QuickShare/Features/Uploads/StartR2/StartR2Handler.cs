using LightResults;


namespace QuickShare.Features.Uploads.StartR2;

public record StartR2Command(string FileName, long FileSize, string ContentType, IReadOnlyList<int> PartNumbers);

public record StartR2Response(Guid DeliveryId, string UploadId, string ObjectKey, IReadOnlyList<R2PartUrl> Parts);

public sealed class StartR2Handler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    // S3/R2 minimum part size (all parts except the last). Caps how many presigned
    // part URLs a client can request for a declared file size.
    internal const long MinPartBytes = 5 * 1024 * 1024;

    internal static int MaxPartsFor(long fileSize)
        => (int)Math.Min(10_000, (fileSize + MinPartBytes - 1) / MinPartBytes + 1);

    public async Task<Result<StartR2Response>> Handle(StartR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;

        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        if (maxFileSize.HasValue && cmd.FileSize > maxFileSize.Value)
            return Result.Failure<StartR2Response>(new LightResults.Error(
                $"File size {cmd.FileSize} exceeds {plan} plan limit of {maxFileSize} bytes"));

        if (cmd.PartNumbers.Count > MaxPartsFor(cmd.FileSize))
            return Result.Failure<StartR2Response>(new LightResults.Error(
                $"Part count {cmd.PartNumbers.Count} exceeds what a {cmd.FileSize}-byte file can need"));

        var deliveryId = Guid.CreateVersion7();

        R2InitResult initResult;
        try
        {
            initResult = await storage.InitR2MultipartUploadAsync(deliveryId, cmd.FileName, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure<StartR2Response>(new LightResults.Error(
                $"R2 init failed: {ex.Message}"));
        }

        IReadOnlyList<R2PartUrl> partUrls;
        try
        {
            partUrls = await storage.GenerateUploadPartUrlsAsync(
                initResult.UploadId, initResult.ObjectKey, cmd.PartNumbers, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure<StartR2Response>(new LightResults.Error(
                $"R2 part URL generation failed: {ex.Message}"));
        }

        var maxExpiryDays = PlanTierLimits.GetMaxExpiryDays(plan);
        var session = new UploadSession
        {
            Id = deliveryId,
            SenderId = user?.Id,
            FileName = cmd.FileName,
            ContentType = cmd.ContentType,
            FileSize = cmd.FileSize,
            Status = "InProgress",
            ExpiresAt = DateTime.UtcNow.AddDays(maxExpiryDays),
            R2UploadId = initResult.UploadId,
            StoragePath = initResult.ObjectKey
        };

        db.UploadSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return Result.Success(new StartR2Response(
            deliveryId, initResult.UploadId, initResult.ObjectKey, partUrls));
    }
}
