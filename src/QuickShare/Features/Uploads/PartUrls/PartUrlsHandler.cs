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
                s.Status == "InProgress", ct);

        if (session is null || string.IsNullOrEmpty(session.StoragePath))
            return Result.Failure<PartUrlsResponse>(new LightResults.Error("No matching in-progress upload session"));

        if (cmd.PartNumbers.Count > StartR2.StartR2Handler.MaxPartsFor(session.FileSize))
            return Result.Failure<PartUrlsResponse>(new LightResults.Error("Part count exceeds what this upload can need"));

        IReadOnlyList<R2PartUrl> parts;
        try
        {
            parts = await storage.GenerateUploadPartUrlsAsync(cmd.UploadId, session.StoragePath, cmd.PartNumbers, ct);
        }
        catch (Exception ex)
        {
            return Result.Failure<PartUrlsResponse>(new LightResults.Error($"R2 part URL generation failed: {ex.Message}"));
        }

        return Result.Success(new PartUrlsResponse(parts));
    }
}
