using LightResults;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Downloads.Preview;

public record PreviewDeliveryQuery(string ShareCode) : IRequest<Result<Uri>>;

public sealed class PreviewDeliveryHandler(IStorageProvider storage, AppDbContext db, DeliveryAccessService access)
    : IRequestHandler<PreviewDeliveryQuery, Result<Uri>>
{
    private static readonly TimeSpan PreviewUrlTtl = TimeSpan.FromHours(1);

    public async ValueTask<Result<Uri>> Handle(PreviewDeliveryQuery query, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        if (delivery is null || delivery.DeletedAt is not null)
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.NotFound));

        // Same scope rules as the byte path (issues #142/#166): a preview is the file.
        if (await access.CheckAsync(delivery, cancellationToken) != DeliveryAccessDecision.Allowed)
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryRestricted));

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
            var url = await storage.GetPresignedDownloadUrlAsync(delivery.StoragePath, PreviewUrlTtl, cancellationToken);
            return Result.Success(url);
        }
        catch
        {
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryNotFound));
        }
    }
}
