using LightResults;

namespace QuickShare.Features.Deliveries.Favorite;

public record ToggleFavoriteCommand(Guid DeliveryId) : IRequest<Result<ToggleFavoriteResponse>>;

public sealed class ToggleFavoriteHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<ToggleFavoriteCommand, Result<ToggleFavoriteResponse>>
{
    public async ValueTask<Result<ToggleFavoriteResponse>> Handle(
        ToggleFavoriteCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<ToggleFavoriteResponse>(new LightResults.Error(ErrorCodes.NotFound));

        delivery.IsFavorite = !delivery.IsFavorite;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new ToggleFavoriteResponse(delivery.IsFavorite));
    }
}
