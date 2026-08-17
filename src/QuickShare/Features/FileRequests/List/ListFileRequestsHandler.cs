using LightResults;

namespace QuickShare.Features.FileRequests.List;

public record ListFileRequestsQuery(
    string? Cursor = null,
    int Limit = FileRequestPaging.DefaultLimit) : IRequest<Result<CursorPage<FileRequestItem>>>;

public record FileRequestItem(
    Guid Id,
    string Code,
    string Title,
    DateTime ExpiresAt,
    DateTime? ClosedAt,
    DateTime CreatedAt,
    long MaxTotalBytes,
    int MaxFiles,
    long CurrentTotalBytes,
    int CurrentFileCount);

public static class FileRequestPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

public sealed class ListFileRequestsHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<ListFileRequestsQuery, Result<CursorPage<FileRequestItem>>>
{
    public async ValueTask<Result<CursorPage<FileRequestItem>>> Handle(
        ListFileRequestsQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var limit = Math.Clamp(query.Limit, 1, FileRequestPaging.MaxLimit);

        // Not cached: the current usage counters change with every submission.
        var source = db.FileRequests
            .AsNoTracking()
            .Where(r => r.OwnerId == user.Id);

        var page = await source
            .ToCursorPageAsync(query.Cursor, limit, r => new FileRequestItem(
                r.Id, r.Code, r.Title, r.ExpiresAt, r.ClosedAt, r.CreatedAt,
                r.MaxTotalBytes, r.MaxFiles,
                r.Deliveries.Where(d => d.Status == DeliveryStatus.Active).Sum(d => (long?)d.TotalBytes) ?? 0,
                r.Deliveries.Count(d => d.Status == DeliveryStatus.Active)), cancellationToken);

        return Result.Success(page);
    }
}
