using LightResults;

namespace QuickShare.Features.Deliveries.List;

public record ListDeliveriesQuery;

public record DeliveryItem(
    Guid Id, string FileName, string ContentType, long FileSize,
    string ShareCode, int DownloadCount, int MaxDownloads,
    DateTime? ExpiresAt, DateTime CreatedAt,
    bool HasBranding
);

public sealed class ListDeliveriesHandler
{
    public async Task<Result<DeliveryItem[]>> Handle(ListDeliveriesQuery _, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);
        var userId = user.Id;
        var items = await db.Deliveries
            .Where(d => d.SenderId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DeliveryItem(d.Id, d.FileName, d.ContentType, d.FileSize, d.ShareCode, d.DownloadCount, d.MaxDownloads, d.ExpiresAt, d.CreatedAt,
                d.BrandLogo != null || d.BrandMessage != null || d.BrandAccent != null))
            .ToArrayAsync(ct);
        return Result.Success(items);
    }
}
