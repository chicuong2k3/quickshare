using LightResults;

namespace QuickShare.Features.Deliveries.List;

public record ListDeliveriesQuery(
    string? Cursor = null,
    int Limit = DeliveryPaging.DefaultLimit,
    string? Status = null,
    string? Search = null,
    bool FavoritesOnly = false);

public record DeliveryItem(
    Guid Id, string FileName, string ContentType, long FileSize,
    string ShareCode, int DownloadCount, int MaxDownloads,
    DateTime? ExpiresAt, DateTime CreatedAt, string Status, DateTime? DeliverAt,
    bool HideDownloadStats, bool IsFavorite, string? Alias
);

public static class DeliveryPaging
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
}

public sealed class ListDeliveriesHandler
{
    public async Task<Result<CursorPage<DeliveryItem>>> Handle(
        ListDeliveriesQuery query, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;
        var limit = Math.Clamp(query.Limit, 1, DeliveryPaging.MaxLimit);

        // Not cached: this list changes on every download, and a stale count here is exactly
        // the bug the page exists to avoid.
        var source = db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == userId);

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
                d.IsFavorite, d.Alias), ct);

        return Result.Success(page);
    }
}
