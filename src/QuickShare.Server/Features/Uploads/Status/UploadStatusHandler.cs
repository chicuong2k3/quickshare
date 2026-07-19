using LightResults;

namespace QuickShare.Server.Features.Uploads.Status;

public record UploadStatusQuery(Guid UploadId);

public record UploadStatusResponse(
    string Status,
    int TotalChunks,
    int[] UploadedChunks,
    int[] MissingChunks,
    DateTime ExpiresAt
);

public sealed class UploadStatusHandler
{
    public async Task<Result<UploadStatusResponse>> Handle(UploadStatusQuery query, AppDbContext db, CancellationToken ct)
    {
        var session = await db.UploadSessions.FindAsync([query.UploadId], ct);
        if (session is null)
            return Result.Failure<UploadStatusResponse>(new NotFoundError("UploadSession", query.UploadId));

        if (session.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<UploadStatusResponse>(new LightResults.Error("Session expired"));

        var uploadedIndices = await db.Chunks
            .Where(c => c.DeliveryId == query.UploadId)
            .OrderBy(c => c.Index)
            .Select(c => c.Index)
            .ToListAsync(ct);

        var totalChunks = (int)Math.Ceiling((double)session.FileSize / UploadConstants.DefaultChunkSize);
        var uploadedSet = new HashSet<int>(uploadedIndices);
        var allIndices = Enumerable.Range(0, totalChunks);
        var missingChunks = allIndices.Where(i => !uploadedSet.Contains(i)).ToArray();

        return Result.Success(new UploadStatusResponse(
            session.Status,
            totalChunks,
            uploadedIndices.ToArray(),
            missingChunks,
            session.ExpiresAt
        ));
    }
}
