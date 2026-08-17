using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Rename;

public record RenameDeliveryCommand(Guid DeliveryId, string FileName) : IRequest<Result>;

/// <summary>
/// Renames a delivery after upload (issue #318). Only the DB row changes — the R2
/// object key stays; the download name comes from Delivery.FileName.
/// </summary>
public sealed class RenameDeliveryHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache)
    : IRequestHandler<RenameDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(RenameDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

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
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}
