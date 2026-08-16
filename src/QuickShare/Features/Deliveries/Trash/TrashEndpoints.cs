using LightResults;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Deliveries.Trash;

public record TrashDeliveryCommand(Guid DeliveryId, bool Restore);

/// <summary>
/// Soft-deletes or restores a delivery (issue #99). Soft-delete sets DeletedAt;
/// the cleanup job purges rows deleted more than 24h ago. The R2 object is left
/// in place until that purge.
/// </summary>
public sealed class TrashDeliveryHandler
{
    public async Task<Result> Handle(TrashDeliveryCommand cmd, AppDbContext db, IHttpContextAccessor http, CacheService cache, CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        if (delivery.Status == DeliveryStatus.Scheduled && !cmd.Restore)
            return Result.Failure(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        delivery.DeletedAt = cmd.Restore ? null : DateTime.UtcNow;
        if (cmd.Restore)
            delivery.Status = DeliveryStatus.Active;

        await db.SaveChangesAsync(ct);
        await cache.RemoveAsync($"download:info:{delivery.ShareCode}", ct);

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

    private async Task RunAsync(bool restore, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(new TrashDeliveryCommand(id, restore), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
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
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result>(new TrashDeliveryCommand(id, Restore: true), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message == ErrorCodes.NotFound ? 404 : 400);
    }
}
