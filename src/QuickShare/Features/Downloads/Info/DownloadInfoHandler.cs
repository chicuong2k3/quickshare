using LightResults;

namespace QuickShare.Features.Downloads.Info;

public record DownloadInfoQuery(string ShareCode);

public record DownloadInfoResponse(
    string FileName,
    string ContentType,
    long FileSize,
    DateTime? ExpiresAt,
    int DownloadCount,
    int MaxDownloads,
    bool HasPassword,
    bool HasPayment,
    string SenderPlan,
    decimal? Price
);

public sealed class DownloadInfoHandler
{
    public async Task<Result<DownloadInfoResponse>> Handle(DownloadInfoQuery query, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .Include(d => d.Sender)
            .FirstOrDefaultAsync(d => d.ShareCode == query.ShareCode, ct);

        if (delivery is null)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("delivery_not_found"));

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("delivery_expired"));

        if (delivery.DownloadCount >= delivery.MaxDownloads)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error("download_limit_reached"));

        var senderPlan = string.IsNullOrEmpty(delivery.Sender?.Plan) ? "anonymous" : delivery.Sender.Plan;

        return Result.Success(new DownloadInfoResponse(
            delivery.FileName,
            delivery.ContentType,
            delivery.FileSize,
            delivery.ExpiresAt,
            delivery.DownloadCount,
            delivery.MaxDownloads,
            HasPassword: !string.IsNullOrEmpty(delivery.PasswordHash),
            HasPayment: delivery.Price.HasValue && delivery.Price > 0,
            SenderPlan: senderPlan,
            Price: delivery.Price
        ));
    }
}
