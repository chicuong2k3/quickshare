using LightResults;

namespace QuickShare.Features.Deliveries.ExtensionRequests;

public record GetExtensionStatusQuery(string Code) : IRequest<Result<ExtensionStatusResponse>>;

public record ExtensionStatusResponse(bool Allowed);

/// <summary>
/// Whether a (possibly expired) delivery accepts extension requests (issue #233).
/// Used by the expired download page to decide whether to show the "request more time" button.
/// </summary>
public sealed class GetExtensionStatusHandler(AppDbContext db)
    : IRequestHandler<GetExtensionStatusQuery, Result<ExtensionStatusResponse>>
{
    public async ValueTask<Result<ExtensionStatusResponse>> Handle(
        GetExtensionStatusQuery query, CancellationToken cancellationToken)
    {
        var allowed = await db.Deliveries
            .AsNoTracking()
            .AnyAsync(d => (d.ShareCode == query.Code || d.Alias == query.Code) && d.AllowExtensionRequests,
                cancellationToken);

        return Result.Success(new ExtensionStatusResponse(allowed));
    }
}
