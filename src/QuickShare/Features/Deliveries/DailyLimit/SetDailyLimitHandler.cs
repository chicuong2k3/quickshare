using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.DailyLimit;

public record SetDailyLimitCommand(Guid DeliveryId, int? Limit);

/// <summary>
/// Anti-leech per-link daily download cap (issue #227). Paid plans only —
/// it's a bandwidth-protection feature for senders who pay for it.
/// </summary>
public sealed class SetDailyLimitHandler
{
    public const int MaxDailyLimit = 10_000;

    public async Task<Result> Handle(
        SetDailyLimitCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CacheService cache,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (cmd.Limit is { } limit && limit is < 1 or > MaxDailyLimit)
            return Result.Failure(new LightResults.Error(ErrorCodes.DailyLimitInvalid));

        delivery.DailyDownloadLimit = cmd.Limit;
        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

        return Result.Success();
    }
}
