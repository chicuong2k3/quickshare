using LightResults;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Deliveries.Purge;

public record PurgeDeliveryCommand(Guid DeliveryId) : IRequest<Result>;

/// <summary>
/// "Delete forever" (issue #157): removes the R2 object and the row immediately,
/// bypassing the 24h trash grace (#99). GDPR right-to-erasure path.
/// </summary>
public sealed class PurgeDeliveryHandler(IStorageProvider storage, ILogger<PurgeDeliveryHandler> logger, AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<PurgeDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(PurgeDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (!string.IsNullOrEmpty(delivery.StoragePath))
        {
            try
            {
                await storage.DeleteObjectsAsync(delivery.StoragePath, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to purge R2 objects for delivery {DeliveryId}", delivery.Id);
            }
        }

        db.Deliveries.Remove(delivery);
        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}

public sealed class PurgeDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/deliveries/{Id}/purge");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Permanently delete a delivery and its stored object";
            s.Description = "Sender only (issue #157). Immediate, irreversible — bypasses the trash grace.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new PurgeDeliveryCommand(id), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 400);
    }
}
