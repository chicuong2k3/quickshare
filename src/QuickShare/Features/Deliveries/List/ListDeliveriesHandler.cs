using LightResults;

namespace QuickShare.Features.Deliveries.List;

public record ListDeliveriesQuery(
    string? Cursor = null,
    int Limit = DeliveryPaging.DefaultLimit,
    string? Status = null,
    string? Search = null,
    bool FavoritesOnly = false, bool DeletedOnly = false) : IRequest<Result<CursorPage<DeliveryItem>>>;

public record DeliveryItem(
    Guid Id, string FileName, string ContentType, long FileSize,
    string ShareCode, int DownloadCount, int MaxDownloads,
    DateTime? ExpiresAt, DateTime CreatedAt, string Status, DateTime? DeliverAt,
    bool HideDownloadStats, bool IsFavorite, string? Alias, string? Tag
)
{
    /// <summary>Active file lock (issue #308). Null when the delivery is not locked.</summary>
    public FileLockInfo? Lock { get; init; }
}

public record FileLockInfo(Guid LockedById, bool LockedBySelf, DateTime ExpiresAt);

public static class DeliveryPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

public sealed class ListDeliveriesHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<ListDeliveriesQuery, Result<CursorPage<DeliveryItem>>>
{
    public async ValueTask<Result<CursorPage<DeliveryItem>>> Handle(
        ListDeliveriesQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var userId = user.Id;
        var limit = Math.Clamp(query.Limit, 1, DeliveryPaging.MaxLimit);

        // Not cached: this list changes on every download, and a stale count here is exactly
        // the bug the page exists to avoid.
        var source = db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == userId);

        source = query.DeletedOnly
            ? source.Where(d => d.DeletedAt != null)
            : source.Where(d => d.DeletedAt == null);

        // Status filter + name search (issue #214). Sort stays newest-first: the cursor is
        // keyed on CreatedAt, so arbitrary sort orders don't compose with keyset paging.
        if (!string.IsNullOrEmpty(query.Status))
            source = source.Where(d => d.Status == query.Status);

        if (query.FavoritesOnly)
            source = source.Where(d => d.IsFavorite);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // ToLower here translates to SQL LOWER() — the analyzer's culture concern
            // doesn't apply inside an EF expression tree.
#pragma warning disable CA1304, CA1311, CA1862
            var term = query.Search.Trim().ToLower();
            source = source.Where(d => d.FileName.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var page = await source
            .ToCursorPageAsync(query.Cursor, limit, d => new DeliveryItem(
                d.Id, d.FileName, d.ContentType, d.FileSize, d.ShareCode,
                d.DownloadCount, d.MaxDownloads, d.ExpiresAt, d.CreatedAt, d.Status, d.DeliverAt, d.HideDownloadStats,
                d.IsFavorite, d.Alias, d.Tag), cancellationToken);

        // Batched lock enrichment (issue #308). ToCursorPageAsync materializes the page
        // before applying the selector, so a correlated per-row lock query would be N+1 —
        // fetch every active lock for the page's deliveries in one round-trip instead.
        var now = DateTime.UtcNow;
        var pageIds = page.Items.Select(i => i.Id).ToArray();
        var activeLocks = await db.FileLocks
            .AsNoTracking()
            .Where(l => pageIds.Contains(l.DeliveryId)
                && l.ReleasedAt == null
                && l.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        var lockByDelivery = activeLocks
            .GroupBy(l => l.DeliveryId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.CreatedAt).First());

        var items = page.Items
            .Select(item => lockByDelivery.TryGetValue(item.Id, out var lockInfo)
                ? item with
                {
                    Lock = new FileLockInfo(lockInfo.LockedById, lockInfo.LockedById == userId, lockInfo.ExpiresAt)
                }
                : item)
            .ToList();

        return Result.Success(new CursorPage<DeliveryItem>(items, page.NextCursor));
    }
}
