using LightResults;

namespace QuickShare.Features.Uploads.PartUrls;

public record PartUrlsCommand(Guid DeliveryId, string UploadId, IReadOnlyList<int> PartNumbers);

public record PartUrlsResponse(IReadOnlyList<R2PartUrl> Parts);

public sealed class PartUrlsHandler(IStorageProvider storage, IHttpContextAccessor httpContextAccessor)
{
    public async Task<Result<PartUrlsResponse>> Handle(PartUrlsCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var user = await httpContextAccessor.HttpContext!.User.GetUserOrDefaultAsync(db, ct);
        var senderId = user?.Id;

        var session = await db.UploadSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s =>
                s.Id == cmd.DeliveryId &&
                s.SenderId == senderId &&
                s.R2UploadId == cmd.UploadId &&
                s.Status == UploadSessionStatus.InProgress, ct);

        if (session is null || string.IsNullOrEmpty(session.StoragePath))
            return Result.Failure<PartUrlsResponse>(new LightResults.Error("No matching in-progress upload session"));

        if (cmd.PartNumbers.Count > StartR2.StartR2Handler.MaxPartsFor(session.FileSize))
            return Result.Failure<PartUrlsResponse>(new LightResults.Error("Part count exceeds what this upload can need"));

        // Presigned URL generation is local (no network) — an exception here is a bug
        // and must surface to the global exception handler, not be masked as an R2 failure.
        var parts = await storage.GenerateUploadPartUrlsAsync(cmd.UploadId, session.StoragePath, cmd.PartNumbers, ct);

        return Result.Success(new PartUrlsResponse(parts));
    }
}
