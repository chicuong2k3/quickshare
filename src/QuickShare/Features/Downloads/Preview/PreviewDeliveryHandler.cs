using LightResults;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Downloads.Preview;

public record PreviewDeliveryQuery(string ShareCode);

public sealed class PreviewDeliveryHandler(IStorageProvider storage)
{
    private static readonly TimeSpan PreviewUrlTtl = TimeSpan.FromHours(1);

    public async Task<Result<Uri>> Handle(PreviewDeliveryQuery query, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null)
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status == DeliveryStatus.Suspended
            || delivery.Status == DeliveryStatus.Revoked
            || delivery.Status == DeliveryStatus.Expired
            || (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow))
        {
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryExpired));
        }

        if (delivery.DownloadCount >= delivery.MaxDownloads)
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DownloadLimitReached));

        if (!string.IsNullOrEmpty(delivery.OtpHash) || !string.IsNullOrEmpty(delivery.PasswordHash) || string.IsNullOrEmpty(delivery.StoragePath))
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryGated));

        try
        {
            var url = await storage.GetPresignedDownloadUrlAsync(delivery.StoragePath, PreviewUrlTtl, ct);
            return Result.Success(url);
        }
        catch
        {
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryNotFound));
        }
    }
}
