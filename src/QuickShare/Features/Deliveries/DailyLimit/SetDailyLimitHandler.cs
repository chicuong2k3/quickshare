using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.DailyLimit;

public record SetDailyLimitCommand(Guid DeliveryId, int? Limit) : IRequest<Result>;

/// <summary>
/// Anti-leech per-link daily download cap (issue #227). Paid plans only —
/// it's a bandwidth-protection feature for senders who pay for it.
/// </summary>
public sealed class SetDailyLimitHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<SetDailyLimitCommand, Result>
{
    public const int MaxDailyLimit = 10_000;

    public async ValueTask<Result> Handle(
        SetDailyLimitCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (cmd.Limit is { } limit && limit is < 1 or > MaxDailyLimit)
            return Result.Failure(new LightResults.Error(ErrorCodes.DailyLimitInvalid));

        delivery.DailyDownloadLimit = cmd.Limit;
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}
