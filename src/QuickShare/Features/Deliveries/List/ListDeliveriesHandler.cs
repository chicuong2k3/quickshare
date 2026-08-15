using LightResults;

namespace QuickShare.Features.Deliveries.List;

public record ListDeliveriesQuery(string? Cursor = null, int Limit = DeliveryPaging.DefaultLimit);

public record DeliveryItem(
    Guid Id, string FileName, string ContentType, long FileSize,
    string ShareCode, int DownloadCount, int MaxDownloads,
    DateTime? ExpiresAt, DateTime CreatedAt, string Status, DateTime? DeliverAt,
    bool HideDownloadStats
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
        var page = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.SenderId == userId)
            .ToCursorPageAsync(query.Cursor, limit, d => new DeliveryItem(
                d.Id, d.FileName, d.ContentType, d.FileSize, d.ShareCode,
                d.DownloadCount, d.MaxDownloads, d.ExpiresAt, d.CreatedAt, d.Status, d.DeliverAt, d.HideDownloadStats), ct);

        return Result.Success(page);
    }
}
