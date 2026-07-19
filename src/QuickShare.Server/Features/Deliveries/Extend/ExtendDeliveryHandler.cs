using LightResults;

namespace QuickShare.Server.Features.Deliveries.Extend;

public record ExtendDeliveryCommand(Guid DeliveryId);

public record ExtendDeliveryResponse(DateTime NewExpiry);

public sealed class ExtendDeliveryHandler
{
    public async Task<Result<ExtendDeliveryResponse>> Handle(ExtendDeliveryCommand cmd, AppDbContext db, IHttpContextAccessor http, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetOrCreateUserAsync(db, ct);
        var userId = user.Id;
        var delivery = await db.Deliveries.FindAsync([cmd.DeliveryId], ct);
        if (delivery is null || delivery.SenderId != userId)
            return Result.Failure<ExtendDeliveryResponse>(new NotFoundError("Delivery", cmd.DeliveryId));

        delivery.ExpiresAt = (delivery.ExpiresAt ?? DateTime.UtcNow).AddHours(24);
        await db.SaveChangesAsync(ct);
        return Result.Success(new ExtendDeliveryResponse(delivery.ExpiresAt.Value));
    }
}
