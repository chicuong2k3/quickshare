using LightResults;


namespace QuickShare.Features.Uploads.AbortR2;

public record AbortR2Command(Guid DeliveryId, string UploadId, string ObjectKey);

public sealed class AbortR2Handler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    public async Task<Result> Handle(AbortR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, ct);

        // Only the session's owner may abort it, and only with matching ids —
        // otherwise anyone who learns a DeliveryId could kill an in-progress upload.
        // Mismatch is a silent no-op: abort is best-effort and this avoids probing.
        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], ct);
        if (session is null ||
            session.Status != "InProgress" ||
            session.R2UploadId != cmd.UploadId ||
            session.SenderId != user?.Id ||
            string.IsNullOrEmpty(session.StoragePath))
        {
            return Result.Success();
        }

        try
        {
            await storage.AbortR2MultipartUploadAsync(cmd.UploadId, session.StoragePath, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure(new LightResults.Error($"R2 abort failed: {ex.Message}"));
        }

        db.UploadSessions.Remove(session);
        await db.SaveChangesAsync(ct);

        return Result.Success();
    }
}
