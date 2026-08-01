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
    bool HasOtp,
    string SenderPlan
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
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryNotFound));

        if (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DeliveryExpired));

        if (delivery.DownloadCount >= delivery.MaxDownloads)
            return Result.Failure<DownloadInfoResponse>(new LightResults.Error(ErrorCodes.DownloadLimitReached));

        var senderPlan = string.IsNullOrEmpty(delivery.Sender?.Plan) ? PlanTierLimits.Anonymous : delivery.Sender.Plan;

        return Result.Success(new DownloadInfoResponse(
            delivery.FileName,
            delivery.ContentType,
            delivery.FileSize,
            delivery.ExpiresAt,
            delivery.DownloadCount,
            delivery.MaxDownloads,
            HasPassword: !string.IsNullOrEmpty(delivery.PasswordHash),
            HasOtp: !string.IsNullOrEmpty(delivery.OtpHash),
            SenderPlan: senderPlan
        ));
    }
}
