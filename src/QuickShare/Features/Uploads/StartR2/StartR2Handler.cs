using LightResults;
using QuickShare.Abstractions;

namespace QuickShare.Features.Uploads.StartR2;

public record StartR2Command(string FileName, long FileSize, string ContentType, IReadOnlyList<int> PartNumbers);

public record StartR2Response(Guid DeliveryId, string UploadId, string ObjectKey, IReadOnlyList<R2PartUrl> Parts);

public sealed class StartR2Handler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    public async Task<Result<StartR2Response>> Handle(StartR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserAsync(db, ct);

        var maxFileSize = PlanTierLimits.GetMaxFileSize(user.Plan);
        if (maxFileSize.HasValue && cmd.FileSize > maxFileSize.Value)
            return Result.Failure<StartR2Response>(new LightResults.Error(
                $"File size {cmd.FileSize} exceeds {user.Plan} plan limit of {maxFileSize} bytes"));

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

        var maxExpiryDays = PlanTierLimits.GetMaxExpiryDays(user.Plan);
        var session = new UploadSession
        {
            Id = deliveryId,
            SenderId = user.Id,
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
