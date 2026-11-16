using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.ExtensionRequests;

public record ApproveExtensionRequestCommand(Guid RequestId) : IRequest<Result<ApproveExtensionRequestResponse>>;

public record ApproveExtensionRequestResponse(DateTime ExpiresAt);

public sealed class ApproveExtensionRequestHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<ApproveExtensionRequestCommand, Result<ApproveExtensionRequestResponse>>
{
    private const double ExtendDays = 7.0;

    public async ValueTask<Result<ApproveExtensionRequestResponse>> Handle(
        ApproveExtensionRequestCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var request = await db.ExtensionRequests
            .FirstOrDefaultAsync(r => r.Id == cmd.RequestId, cancellationToken);

        if (request is null)
            return Result.Failure<ApproveExtensionRequestResponse>(new LightResults.Error(ErrorCodes.ExtensionRequestNotFound));

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == request.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<ApproveExtensionRequestResponse>(new LightResults.Error(ErrorCodes.NotFound));

        if (request.Status != ExtensionRequestStatus.Pending)
            return Result.Success(new ApproveExtensionRequestResponse(delivery.ExpiresAt ?? DateTime.UtcNow));

        // Retention policy (issue #161): the approval is the owner's, but a recipient
        // triggered the request — so the window is read off the delivery's sender, fresh,
        // because the cached User entity can still hold the previous value.
        var policy = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == delivery.SenderId)
            .Select(u => new { u.RetentionDays, u.RetentionNoExtend })
            .FirstOrDefaultAsync(cancellationToken);

        // Regulated mode: approving would buy no time, so say so instead of no-op'ing.
        if (policy is { RetentionNoExtend: true } && RetentionPolicy.Exceeds(delivery.ExpiresAt, policy.RetentionDays))
            return Result.Failure<ApproveExtensionRequestResponse>(new LightResults.Error(ErrorCodes.RetentionPolicyLocked));

        // Extend by 7 days, capped at the plan maximum (mirrors #45), then clamped to the
        // account retention window — the compliance ceiling sits above the plan cap.
        var now = DateTime.UtcNow;
        var newExpiry = now.AddDays(ExtendDays);
        var cap = now.Add(PlanTierLimits.GetMaxExpiry(user.Plan));
        if (newExpiry > cap)
            newExpiry = cap;
        newExpiry = RetentionPolicy.Clamp(newExpiry, policy?.RetentionDays);

        delivery.ExpiresAt = newExpiry;
        delivery.Status = DeliveryStatus.Active;
        request.Status = ExtensionRequestStatus.Approved;
        request.RespondedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success(new ApproveExtensionRequestResponse(newExpiry));
    }
}
