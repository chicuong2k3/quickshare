using LightResults;

namespace QuickShare.Features.Deliveries.Favorite;

public record ToggleFavoriteCommand(Guid DeliveryId);

public sealed class ToggleFavoriteHandler
{
    public async Task<Result<ToggleFavoriteResponse>> Handle(
        ToggleFavoriteCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure<ToggleFavoriteResponse>(new LightResults.Error(ErrorCodes.NotFound));

        delivery.IsFavorite = !delivery.IsFavorite;
        await db.SaveChangesAsync(ct);

        return Result.Success(new ToggleFavoriteResponse(delivery.IsFavorite));
    }
}
