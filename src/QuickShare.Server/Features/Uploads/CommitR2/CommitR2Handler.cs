using System.Security.Cryptography;
using LightResults;
using QuickShare.Server.Abstractions;

namespace QuickShare.Server.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts);

public record CommitR2Response(string ShareCode, string ShareLink);

public sealed class CommitR2Handler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    public async Task<Result<CommitR2Response>> Handle(CommitR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        CompletionResult completionResult;
        try
        {
            completionResult = await storage.CompleteR2MultipartUploadAsync(
                cmd.UploadId, cmd.ObjectKey, cmd.Parts, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                $"R2 commit failed: {ex.Message}"));
        }

        if (!completionResult.Success)
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                completionResult.ErrorMessage ?? "R2 commit returned failure"));

        var user = await httpContextAccessor.HttpContext!.User.GetUserAsync(db, ct);

        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var shareCode = Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var delivery = new Delivery
        {
            Id = cmd.DeliveryId,
            SenderId = user.Id,
            FileName = cmd.FileName,
            ContentType = cmd.ContentType,
            FileSize = cmd.FileSize,
            TotalBytes = cmd.FileSize,
            ChunkCount = cmd.Parts.Count,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(user.Plan),
            StoragePath = cmd.ObjectKey,
            ExpiresAt = DateTime.UtcNow.AddDays(PlanTierLimits.GetMaxExpiryDays(user.Plan))
        };

        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], ct);
        if (session is not null)
        {
            session.Status = "Committed";
            session.R2UploadId = null;
        }

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        return Result.Success(new CommitR2Response(shareCode, $"{baseUrl}/d/{shareCode}"));
    }
}
