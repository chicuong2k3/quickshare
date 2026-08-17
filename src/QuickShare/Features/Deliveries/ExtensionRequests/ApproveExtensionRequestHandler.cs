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

        // Extend by 7 days, capped at the plan maximum (mirrors #45).
        var now = DateTime.UtcNow;
        var newExpiry = now.AddDays(ExtendDays);
        var cap = now.Add(PlanTierLimits.GetMaxExpiry(user.Plan));
        if (newExpiry > cap)
            newExpiry = cap;

        delivery.ExpiresAt = newExpiry;
        delivery.Status = DeliveryStatus.Active;
        request.Status = ExtensionRequestStatus.Approved;
        request.RespondedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success(new ApproveExtensionRequestResponse(newExpiry));
    }
}
