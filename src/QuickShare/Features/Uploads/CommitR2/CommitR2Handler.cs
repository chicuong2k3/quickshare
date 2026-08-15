using LightResults;
using QuickShare.Features.Uploads.Shared;
using System.Security.Cryptography;


namespace QuickShare.Features.Uploads.CommitR2;

public record CommitR2Command(Guid DeliveryId, string UploadId, string ObjectKey, string FileName, string ContentType, long FileSize, IReadOnlyList<R2PartEtag> Parts, string? Otp, string? Source);

public record CommitR2Response(string ShareCode, string ShareLink, DateTime ExpiresAt);

public sealed class CommitR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    ILogger<CommitR2Handler> logger)
{
    // Client-side AES-GCM adds a 12-byte nonce + 16-byte tag on top of the declared plain size.
    private const long EncryptionOverheadSlack = 1024;

    public async Task<Result<CommitR2Response>> Handle(CommitR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;

        // The commit must match the session created by start-r2, for the same owner —
        // otherwise anyone who learns a DeliveryId could hijack or finalize the upload.
        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], ct);
        if (session is null ||
            session.Status != "InProgress" ||
            session.R2UploadId != cmd.UploadId ||
            session.StoragePath != cmd.ObjectKey ||
            session.SenderId != user?.Id)
        {
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                "No matching in-progress upload session"));
        }

        CompletionResult completionResult;
        try
        {
            completionResult = await storage.CompleteR2MultipartUploadAsync(
                cmd.UploadId, cmd.ObjectKey, cmd.Parts, ct);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "R2 commit failed for delivery {DeliveryId}", cmd.DeliveryId);
            return Result.Failure<CommitR2Response>(new LightResults.Error(UploadErrors.R2Upstream + "commit failed"));
        }

        if (!completionResult.Success)
            return Result.Failure<CommitR2Response>(new LightResults.Error(
                completionResult.ErrorMessage ?? "R2 commit returned failure"));

        // Enforce the plan limit on what actually landed in storage, not the declared size —
        // the declared size is client-controlled and part uploads are not size-constrained.
        var maxFileSize = PlanTierLimits.GetMaxFileSize(plan);
        if (maxFileSize.HasValue)
        {
            var actualSize = await storage.GetObjectSizeAsync(cmd.ObjectKey, ct);
            if (actualSize > maxFileSize.Value + EncryptionOverheadSlack)
            {
                try
                {
                    await storage.DeleteObjectsAsync(cmd.ObjectKey, ct);
                }
                catch { /* best-effort cleanup; the sweeper handles leftovers */ }
                return Result.Failure<CommitR2Response>(new LightResults.Error(
                    $"Uploaded size {actualSize} exceeds {plan} plan limit of {maxFileSize} bytes"));
            }
        }

        var randomBytes = new byte[8];
        RandomNumberGenerator.Fill(randomBytes);
        var shareCode = Convert.ToBase64String(randomBytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var expiresAt = DateTime.UtcNow.Add(PlanTierLimits.GetMaxExpiry(plan));

        var delivery = new Delivery
        {
            Id = cmd.DeliveryId,
            SenderId = user?.Id,
            // Metadata comes from the validated session, not the commit request.
            FileName = session.FileName,
            ContentType = session.ContentType,
            FileSize = session.FileSize,
            TotalBytes = session.FileSize,
            ChunkCount = cmd.Parts.Count,
            ShareCode = shareCode,
            MaxDownloads = PlanTierLimits.GetMaxDownloads(plan),
            StoragePath = cmd.ObjectKey,
            ExpiresAt = expiresAt,
            OtpHash = !string.IsNullOrWhiteSpace(cmd.Otp) ? BCrypt.Net.BCrypt.HashPassword(cmd.Otp) : null,
            Source = DeliverySources.Normalize(cmd.Source)
        };

        session.Status = "Committed";
        session.R2UploadId = null;

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var baseUrl = $"{httpContextAccessor.HttpContext!.Request.Scheme}://{httpContextAccessor.HttpContext.Request.Host}";
        return Result.Success(new CommitR2Response(shareCode, $"{baseUrl}/d/{shareCode}", expiresAt));
    }
}
