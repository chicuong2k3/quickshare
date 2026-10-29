using LightResults;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;
using QuickShare.Features.Workspaces.Domains;

namespace QuickShare.Features.Downloads.Preview;

public record PreviewDeliveryQuery(string ShareCode) : IRequest<Result<Uri>>;

public sealed class PreviewDeliveryHandler(IStorageProvider storage, AppDbContext db, DeliveryAccessService access,
    WorkspaceDomainReader domains, IHttpContextAccessor http)
    : IRequestHandler<PreviewDeliveryQuery, Result<Uri>>
{
    private static readonly TimeSpan PreviewUrlTtl = TimeSpan.FromHours(1);

    public async ValueTask<Result<Uri>> Handle(PreviewDeliveryQuery query, CancellationToken cancellationToken)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(query.ShareCode)
            .FirstOrDefaultAsync(cancellationToken);

        var error = await PreviewAccessGate.CheckAsync(delivery, db, access, domains, http.HttpContext, cancellationToken);
        if (error is not null)
        {
            return Result.Failure<Uri>(new LightResults.Error(error));
        }

        var ttl = PreviewUrlTtl;
        if (delivery!.ExpiresAt is { } expiresAt)
        {
            var remaining = expiresAt - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryExpired));
            }
            ttl = remaining < ttl ? remaining : ttl;
        }

        try
        {
            var url = await storage.GetPresignedDownloadUrlAsync(delivery.StoragePath!, ttl, cancellationToken);
            return Result.Success(url);
        }
        catch
        {
            return Result.Failure<Uri>(new LightResults.Error(ErrorCodes.DeliveryNotFound));
        }
    }
}
