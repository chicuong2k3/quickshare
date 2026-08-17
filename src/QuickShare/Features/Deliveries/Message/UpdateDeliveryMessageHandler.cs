using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Message;

public record UpdateDeliveryMessageCommand(Guid DeliveryId, string? Message) : IRequest<Result>;

public sealed class UpdateDeliveryMessageHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<UpdateDeliveryMessageCommand, Result>
{
    public async ValueTask<Result> Handle(
        UpdateDeliveryMessageCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.DownloadCount > 0)
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryAlreadyDownloaded));

        var normalized = NormalizeMessage(cmd.Message);
        if (normalized is not null && normalized.Length > Delivery.MessageMaxLength)
            return Result.Failure(new LightResults.Error(ErrorCodes.MessageTooLong));

        delivery.Message = normalized;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }

    private static string? NormalizeMessage(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        return message.Trim();
    }
}
