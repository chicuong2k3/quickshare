using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Trash;

public record TrashDeliveryCommand(Guid DeliveryId, bool Restore) : IRequest<Result>;

/// <summary>
/// Soft-deletes or restores a delivery (issue #99). Soft-delete sets DeletedAt;
/// the cleanup job purges rows deleted more than 24h ago. The R2 object is left
/// in place until that purge.
/// </summary>
public sealed class TrashDeliveryHandler(AppDbContext db, IHttpContextAccessor http, CacheService cache)
    : IRequestHandler<TrashDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(TrashDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status == DeliveryStatus.Scheduled && !cmd.Restore)
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        delivery.DeletedAt = cmd.Restore ? null : DateTime.UtcNow;
        if (cmd.Restore)
            delivery.Status = DeliveryStatus.Active;

        await db.SaveChangesAsync(cancellationToken);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/delete");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Soft-delete a delivery (moves to trash)";
            s.Description = "Sender only (issue #99). Restorable for 24h.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await RunAsync(restore: false, ct);
    }

    private async Task RunAsync(bool restore, CancellationToken cancellationToken)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new TrashDeliveryCommand(id, restore), cancellationToken);

        if (result.IsSuccess())
            await Send.NoContentAsync(cancellationToken);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 400);
    }
}

public sealed class RestoreDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/restore");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Restore a soft-deleted delivery";
            s.Description = "Sender only (issue #99).";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(new TrashDeliveryCommand(id, Restore: true), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 400);
    }
}
