using LightResults;

namespace QuickShare.Server.Features.Downloads.Info;

public record DownloadInfoQuery(string ShareCode);

public record DownloadInfoResponse(
    string FileName,
    string ContentType,
    long FileSize,
    DateTime? ExpiresAt,
    int DownloadCount,
    int MaxDownloads
);

public sealed class DownloadInfoHandler
{
    public async Task<Result<DownloadInfoResponse>> Handle(DownloadInfoQuery query, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("delivery_not_found"));

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("delivery_expired"));

        if (delivery.DownloadCount >= delivery.MaxDownloads)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("download_limit_reached"));

        return Result.Success(new DownloadInfoResponse(
            delivery.FileName,
            delivery.ContentType,
            delivery.FileSize,
            delivery.ExpiresAt,
            delivery.DownloadCount,
            delivery.MaxDownloads
        ));
    }
}
