using LightResults;

namespace QuickShare.Features.Deliveries.ExtensionRequests;

public record RejectExtensionRequestCommand(Guid RequestId, string? Message) : IRequest<Result>;

public sealed class RejectExtensionRequestHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<RejectExtensionRequestCommand, Result>
{
    public async ValueTask<Result> Handle(RejectExtensionRequestCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var request = await db.ExtensionRequests
            .FirstOrDefaultAsync(r => r.Id == cmd.RequestId, cancellationToken);

        if (request is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.ExtensionRequestNotFound));

        var ownsDelivery = await db.Deliveries
            .AnyAsync(d => d.Id == request.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (!ownsDelivery)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (request.Status != ExtensionRequestStatus.Pending)
            return Result.Success();

        request.Status = ExtensionRequestStatus.Rejected;
        request.RespondedAt = DateTime.UtcNow;
        request.RejectMessage = string.IsNullOrWhiteSpace(cmd.Message)
            ? null
            : cmd.Message.Trim()[..Math.Min(cmd.Message.Trim().Length, ExtensionRequest.RejectMessageMaxLength)];

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
