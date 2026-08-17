using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Extend;

public record ExtendDeliveryCommand(Guid DeliveryId) : IRequest<Result<ExtendDeliveryResponse>>;

public record ExtendDeliveryResponse(DateTime ExpiresAt);

/// <summary>
/// Extends a delivery's expiry by 7 days without re-uploading (issue #45).
/// Free plans may only extend near (or just after) expiry; paid plans anytime,
/// both capped at the plan's maximum expiry.
/// </summary>
public sealed class ExtendDeliveryHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<ExtendDeliveryCommand, Result<ExtendDeliveryResponse>>
{
    private const double ExtendDays = 7.0;
    private const int FreeExtendWindowHours = 24;
    private const int ExpiredGraceHours = 72;

    public async ValueTask<Result<ExtendDeliveryResponse>> Handle(
        ExtendDeliveryCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var plan = user.Plan;

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<ExtendDeliveryResponse>(new LightResults.Error(ErrorCodes.NotFound));

        // Revoked links lost their stored bytes — nothing left to extend.
        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
            return Result.Failure<ExtendDeliveryResponse>(new LightResults.Error(ErrorCodes.ExtendNotAllowed));

        var now = DateTime.UtcNow;

        // Free (and any non-paid) tiers can only extend near or just after expiry —
        // otherwise "extend" becomes a free way to pin a link forever.
        if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            if (delivery.ExpiresAt is not { } expiresAt)
                return Result.Failure<ExtendDeliveryResponse>(new LightResults.Error(ErrorCodes.ExtendNotAllowed));

            var untilExpiry = expiresAt - now;
            if (untilExpiry > TimeSpan.FromHours(FreeExtendWindowHours))
                return Result.Failure<ExtendDeliveryResponse>(new LightResults.Error(ErrorCodes.ExtendTooEarly));
            if (untilExpiry < TimeSpan.FromHours(-ExpiredGraceHours))
                return Result.Failure<ExtendDeliveryResponse>(new LightResults.Error(ErrorCodes.ExtendTooLate));
        }

        var newExpiry = now.AddDays(ExtendDays);
        var cap = now.Add(PlanTierLimits.GetMaxExpiry(plan));
        if (newExpiry > cap)
            newExpiry = cap;

        delivery.ExpiresAt = newExpiry;
        delivery.Status = DeliveryStatus.Active;
        await db.SaveChangesAsync(cancellationToken);

        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success(new ExtendDeliveryResponse(newExpiry));
    }
}
