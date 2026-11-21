using LightResults;

namespace QuickShare.Features.Deliveries.List;

public record ListDeliveriesQuery(
    string? Cursor = null,
    int Limit = DeliveryPaging.DefaultLimit,
    string? Status = null,
    string? Search = null,
    bool FavoritesOnly = false, bool DeletedOnly = false,
    Guid? FolderId = null) : IRequest<Result<CursorPage<DeliveryItem>>>;

public record DeliveryItem(
    Guid Id, string FileName, string ContentType, long FileSize,
    string ShareCode, int DownloadCount, int MaxDownloads,
    DateTime? ExpiresAt, DateTime CreatedAt, string Status, DateTime? DeliverAt,
    bool HideDownloadStats, bool IsFavorite, string? Alias, string? Tag
);

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

        var workspaceId = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        var source = db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == userId
                || (workspaceId != null && d.WorkspaceId == workspaceId));

        if (query.FolderId is { } folder)
            source = source.Where(d => d.FolderId == folder && d.WorkspaceId == workspaceId);

        source = query.DeletedOnly
            ? source.Where(d => d.DeletedAt != null)
            : source.Where(d => d.DeletedAt == null);

        if (!string.IsNullOrEmpty(query.Status))
            source = source.Where(d => d.Status == query.Status);

        if (query.FavoritesOnly)
            source = source.Where(d => d.IsFavorite);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
#pragma warning disable CA1304, CA1311, CA1862
            var term = query.Search.Trim().ToLower();
            // Full-text search across filename, tag, message and folder name (issue #307).
            // Privacy mode (#187) deliveries are never matched by content or message.
            source = source.Where(d =>
                d.FileName.ToLower().Contains(term)
                || (d.Tag != null && d.Tag.ToLower().Contains(term))
                || (!d.PrivacyMode && d.Message != null && d.Message.ToLower().Contains(term))
                || (d.FolderId != null && db.WorkspaceFolders.Any(f => f.Id == d.FolderId && f.Name.ToLower().Contains(term))));
#pragma warning restore CA1304, CA1311, CA1862
        }

        var page = await source
            .ToCursorPageAsync(query.Cursor, limit, d => new DeliveryItem(
                d.Id, d.FileName, d.ContentType, d.FileSize, d.ShareCode,
                d.DownloadCount, d.MaxDownloads, d.ExpiresAt, d.CreatedAt, d.Status, d.DeliverAt, d.HideDownloadStats,
                d.IsFavorite, d.Alias, d.Tag), cancellationToken);

        return Result.Success(page);
    }
}
