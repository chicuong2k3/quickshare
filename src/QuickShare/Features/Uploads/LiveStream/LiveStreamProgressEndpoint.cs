using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Uploads.LiveStream;

// -----------------------------------------------------------------
// #32 — P2P / Wormhole-style live upload streaming progress update
// The recipient is already holding the link while parts are still landing, so progress is
// reported by the sender and read back from the session row rather than guessed client-side.
// -----------------------------------------------------------------

public sealed class LiveStreamProgressRequest
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public int UploadedPartsCount { get; set; }
    public long UploadedBytes { get; set; }
}

public sealed record LiveStreamProgressResponse(
    string ShareCode,
    int UploadedPartsCount,
    int TotalPartsCount,
    long UploadedBytes,
    int ProgressPercent);

public sealed class UpdateLiveStreamProgressEndpoint(
    AppDbContext db,
    CacheService cache) : Endpoint<LiveStreamProgressRequest, LiveStreamProgressResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.LiveStreamProgressApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(LiveStreamProgressRequest req, CancellationToken ct)
    {
        var session = await db.UploadSessions.FindAsync([req.DeliveryId], ct);
        if (session is null || session.Status != UploadSessionStatus.InProgress || session.R2UploadId != req.UploadId)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        session.UploadedPartsCount = Math.Clamp(req.UploadedPartsCount, 0, Math.Max(1, session.TotalPartsCount));
        session.UploadedBytes = Math.Clamp(req.UploadedBytes, 0L, session.FileSize);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(session.ShareCode))
            await cache.RemoveAsync($"download:info:{session.ShareCode}", ct);

        var totalParts = Math.Max(1, session.TotalPartsCount);
        var pct = Math.Clamp((int)Math.Round(session.UploadedPartsCount * 100.0 / totalParts), 0, 99);

        await Send.OkAsync(new LiveStreamProgressResponse(
            session.ShareCode ?? string.Empty,
            session.UploadedPartsCount,
            totalParts,
            session.UploadedBytes,
            pct), ct);
    }
}
