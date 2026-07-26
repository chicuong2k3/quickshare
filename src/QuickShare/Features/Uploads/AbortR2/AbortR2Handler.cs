using LightResults;
using QuickShare.Abstractions;

namespace QuickShare.Features.Uploads.AbortR2;

public record AbortR2Command(Guid DeliveryId, string UploadId, string ObjectKey);

public sealed class AbortR2Handler(IStorageProvider storage)
{
    public async Task<Result> Handle(AbortR2Command cmd, AppDbContext db, CancellationToken ct)
    {
        try
        {
            await storage.AbortR2MultipartUploadAsync(cmd.UploadId, cmd.ObjectKey, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure(new LightResults.Error($"R2 abort failed: {ex.Message}"));
        }

        var session = await db.UploadSessions.FindAsync([cmd.DeliveryId], ct);
        if (session is not null)
        {
            db.UploadSessions.Remove(session);
            await db.SaveChangesAsync(ct);
        }

        return Result.Success();
    }
}
