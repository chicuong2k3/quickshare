using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.FileRequests.Get;

public record GetFileRequestQuery(string Code) : IRequest<Result<GetFileRequestResponse>>;

public record GetFileRequestResponse(
    string Code,
    string Title,
    string? Message,
    string OwnerName,
    long MaxTotalBytes,
    int MaxFiles,
    DateTime ExpiresAt,
    bool IsClosed,
    long CurrentTotalBytes,
    int CurrentFileCount);

public sealed class GetFileRequestHandler(AppDbContext db, CacheService cache)
    : IRequestHandler<GetFileRequestQuery, Result<GetFileRequestResponse>>
{
    private const string CacheKeyPrefix = "file-request:info:";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(15);

    public async ValueTask<Result<GetFileRequestResponse>> Handle(
        GetFileRequestQuery query,
        CancellationToken cancellationToken)
    {
        var response = await cache.GetOrSetAsync(
            CacheKeyPrefix + query.Code,
            (ct) => LoadAsync(query.Code, ct),
            CacheDuration,
            cancellationToken);

        if (response is null)
            return Result.Failure<GetFileRequestResponse>(new LightResults.Error(ErrorCodes.FileRequestNotFound));

        if (response.ExpiresAt < DateTime.UtcNow)
            return Result.Failure<GetFileRequestResponse>(new LightResults.Error(ErrorCodes.FileRequestExpired));

        if (response.IsClosed)
            return Result.Failure<GetFileRequestResponse>(new LightResults.Error(ErrorCodes.FileRequestClosed));

        return Result.Success(response);
    }

    private async Task<GetFileRequestResponse?> LoadAsync(string code, CancellationToken ct)
    {
        var request = await db.FileRequests
            .AsNoTracking()
            .Where(r => r.Code == code)
            .Select(r => new
            {
                r.Code,
                r.Title,
                r.Message,
                OwnerName = r.Owner!.Name,
                r.MaxTotalBytes,
                r.MaxFiles,
                r.ExpiresAt,
                r.ClosedAt,
                CurrentTotalBytes = r.Deliveries
                    .Where(d => d.Status == DeliveryStatus.Active)
                    .Sum(d => (long?)d.TotalBytes) ?? 0,
                CurrentFileCount = r.Deliveries.Count(d => d.Status == DeliveryStatus.Active),
            })
            .FirstOrDefaultAsync(ct);

        if (request is null)
            return null;

        return new GetFileRequestResponse(
            request.Code,
            request.Title,
            request.Message,
            request.OwnerName,
            request.MaxTotalBytes,
            request.MaxFiles,
            request.ExpiresAt,
            request.ClosedAt is not null,
            request.CurrentTotalBytes,
            request.CurrentFileCount);
    }
}
