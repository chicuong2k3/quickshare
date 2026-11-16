using System.Text.Json;
using LightResults;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Users.Retention;

public record UpdateRetentionPolicyCommand(int? RetentionDays, bool NoExtend)
    : IRequest<Result<UpdateRetentionPolicyResponse>>;

public sealed record UpdateRetentionPolicyResponse(int? RetentionDays, bool NoExtend);

/// <summary>
/// Sets the account-wide retention window (issue #161). The window is a ceiling, not a
/// per-delivery expiry: uploads clamp to it, extends clamp to it, and the expiry sweep
/// converges rows that predate it. A paid control — a free account stays unpolicied.
/// </summary>
public sealed class UpdateRetentionPolicyHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache,
    AnalyticsService analytics)
    : IRequestHandler<UpdateRetentionPolicyCommand, Result<UpdateRetentionPolicyResponse>>
{
    public async ValueTask<Result<UpdateRetentionPolicyResponse>> Handle(
        UpdateRetentionPolicyCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        if (user.Plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
            return Result.Failure<UpdateRetentionPolicyResponse>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        if (!RetentionPolicy.IsValid(cmd.RetentionDays))
            return Result.Failure<UpdateRetentionPolicyResponse>(new LightResults.Error(ErrorCodes.RetentionDaysInvalid));

        var retentionDays = cmd.RetentionDays;

        // Clearing the window clears the extension lock with it — there is no ceiling
        // left to lock extends against, and a stranded flag would silently bite later.
        var noExtend = retentionDays is not null && cmd.NoExtend;

        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.RetentionDays, retentionDays)
                .SetProperty(u => u.RetentionNoExtend, noExtend), cancellationToken);

        // ExecuteUpdateAsync bypasses the change tracker, so the 5-minute User cache still
        // holds the previous window — every clamp site reads it, so it must go now.
        await UserCacheInvalidator.EvictAsync(db, user.Id, cancellationToken);

        // The dry-run preview is cached per window; the settings UI only ever offers the
        // presets, so evicting those keys covers every entry the card can have produced.
        foreach (var preset in RetentionPolicy.Presets)
        {
            await cache.RemoveAsync(RetentionPreviewHandler.CacheKey(user.Id, preset), cancellationToken);
        }

        var metadata = JsonSerializer.Serialize(new { retentionDays, noExtend });
        var userAgent = http.HttpContext!.Request.Headers.UserAgent.ToString();
        var ip = http.HttpContext!.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        await SecurityEventService.AppendAsync(db, user.Id, SecurityEventType.RetentionPolicyChanged, userAgent, ip, cancellationToken, metadata);

        await analytics.RecordAsync(AnalyticsEvents.RetentionPolicyChanged,
            new AnalyticsEventData(Plan: user.Plan), cancellationToken);

        return Result.Success(new UpdateRetentionPolicyResponse(retentionDays, noExtend));
    }
}
