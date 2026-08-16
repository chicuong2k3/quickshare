using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Rename;

public record RenameDeliveryCommand(Guid DeliveryId, string FileName);

/// <summary>
/// Renames a delivery after upload (issue #318). Only the DB row changes — the R2
/// object key stays; the download name comes from Delivery.FileName.
/// </summary>
public sealed class RenameDeliveryHandler
{
    public async Task<Result> Handle(
        RenameDeliveryCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CacheService cache,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        var name = cmd.FileName?.Trim() ?? string.Empty;
        if (name.Length == 0
            || name.Length > Delivery.FileNameMaxLength
            || name.IndexOfAny(['/', '\\']) >= 0
            || name.Contains('\0'))
        {
            return Result.Failure(new LightResults.Error(ErrorCodes.FileNameInvalid));
        }

        delivery.FileName = name;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success();
    }
}
