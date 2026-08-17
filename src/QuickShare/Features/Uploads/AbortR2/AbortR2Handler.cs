using LightResults;
using QuickShare.Features.Uploads.Shared;


namespace QuickShare.Features.Uploads.AbortR2;

public record AbortR2Command(Guid DeliveryId, string UploadId, string ObjectKey) : IRequest<Result>;

public sealed class AbortR2Handler(
    IStorageProvider storage,
    IHttpContextAccessor httpContextAccessor,
    ILogger<AbortR2Handler> logger,
    AppDbContext db)
    : IRequestHandler<AbortR2Command, Result>
{
    public async ValueTask<Result> Handle(AbortR2Command cmd, CancellationToken cancellationToken)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, cancellationToken);

        // Only the session's owner may abort it, and only with matching ids —
        // otherwise anyone who learns a DeliveryId could kill an in-progress upload.
        // Mismatch is a silent no-op: abort is best-effort and this avoids probing.
        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], cancellationToken);
        if (session is null ||
            session.Status != UploadSessionStatus.InProgress ||
            session.R2UploadId != cmd.UploadId ||
            session.SenderId != user?.Id ||
            string.IsNullOrEmpty(session.StoragePath))
        {
            return Result.Success();
        }

        try
        {
            await storage.AbortR2MultipartUploadAsync(cmd.UploadId, session.StoragePath, cancellationToken);
        }
        catch (R2StorageException ex)
        {
            logger.LogError(ex, "R2 abort failed for delivery {DeliveryId}", cmd.DeliveryId);
            return Result.Failure(new LightResults.Error(UploadErrors.R2Upstream + "abort failed"));
        }

        db.UploadSessions.Remove(session);
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
