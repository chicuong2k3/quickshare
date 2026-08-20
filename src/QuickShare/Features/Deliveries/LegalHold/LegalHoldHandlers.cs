using System.Text.Json;
using LightResults;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.LegalHold;

public record PlaceLegalHoldCommand(Guid DeliveryId, string? Reason)
    : IRequest<Result<LegalHoldResponse>>;

public sealed record LegalHoldResponse(Guid DeliveryId, DateTime? HeldAt, string? Reason);

/// <summary>
/// Places a legal hold on a delivery (issue #266). A hold freezes the row and its stored
/// object against every deletion path — retention clamp, trash, purge, expiry sweep,
/// account deletion — until an owner lifts it by hand. There is deliberately no
/// auto-expiry: a hold that lapses on its own is evidence loss during active litigation.
///
/// Owner-only. The case reference is required, because a hold without one cannot be
/// defended to counsel, and both transitions land on the append-only security timeline.
/// </summary>
public sealed class PlaceLegalHoldHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache,
    AnalyticsService analytics)
    : IRequestHandler<PlaceLegalHoldCommand, Result<LegalHoldResponse>>
{
    public async ValueTask<Result<LegalHoldResponse>> Handle(
        PlaceLegalHoldCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var reason = cmd.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
            return Result.Failure<LegalHoldResponse>(new LightResults.Error(ErrorCodes.LegalHoldReasonRequired));

        if (reason.Length > Delivery.LegalHoldReasonMaxLength)
            reason = reason[..Delivery.LegalHoldReasonMaxLength];

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<LegalHoldResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.LegalHoldAt is not null)
            return Result.Failure<LegalHoldResponse>(new LightResults.Error(ErrorCodes.LegalHoldActive));

        delivery.LegalHoldAt = DateTime.UtcNow;
        delivery.LegalHoldReason = reason;
        delivery.LegalHoldById = user.Id;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        await LegalHoldAudit.RecordAsync(
            db, http, analytics, user.Id, delivery, SecurityEventType.LegalHoldPlaced,
            AnalyticsEvents.LegalHoldPlaced, reason, cancellationToken);

        return Result.Success(new LegalHoldResponse(delivery.Id, delivery.LegalHoldAt, delivery.LegalHoldReason));
    }
}

public record LiftLegalHoldCommand(Guid DeliveryId) : IRequest<Result<LegalHoldResponse>>;

/// <summary>
/// Lifts a legal hold (issue #266). Owner-only and manual by design — the caller is the
/// only party who can decide the probe is over. The lifted case reference is carried into
/// the audit row before it is cleared, so the timeline still shows what was released.
/// </summary>
public sealed class LiftLegalHoldHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    CacheService cache,
    AnalyticsService analytics)
    : IRequestHandler<LiftLegalHoldCommand, Result<LegalHoldResponse>>
{
    public async ValueTask<Result<LegalHoldResponse>> Handle(
        LiftLegalHoldCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<LegalHoldResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.LegalHoldAt is null)
            return Result.Failure<LegalHoldResponse>(new LightResults.Error(ErrorCodes.LegalHoldNotActive));

        var liftedReason = delivery.LegalHoldReason;

        delivery.LegalHoldAt = null;
        delivery.LegalHoldReason = null;
        delivery.LegalHoldById = null;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        await LegalHoldAudit.RecordAsync(
            db, http, analytics, user.Id, delivery, SecurityEventType.LegalHoldLifted,
            AnalyticsEvents.LegalHoldLifted, liftedReason, cancellationToken);

        return Result.Success(new LegalHoldResponse(delivery.Id, null, null));
    }
}

/// <summary>Shared audit write for both hold transitions (issue #266).</summary>
internal static class LegalHoldAudit
{
    public static async Task RecordAsync(
        AppDbContext db,
        IHttpContextAccessor http,
        AnalyticsService analytics,
        Guid userId,
        Delivery delivery,
        SecurityEventType eventType,
        string analyticsEvent,
        string? reason,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new { deliveryId = delivery.Id, reason });
        var userAgent = http.HttpContext!.Request.Headers.UserAgent.ToString();
        var ip = http.HttpContext!.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        await SecurityEventService.AppendAsync(db, userId, eventType, userAgent, ip, cancellationToken, metadata);

        await analytics.RecordAsync(analyticsEvent,
            new AnalyticsEventData(FileName: delivery.FileName), cancellationToken);
    }
}
