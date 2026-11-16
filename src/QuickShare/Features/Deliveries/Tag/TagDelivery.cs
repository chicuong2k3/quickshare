using LightResults;

namespace QuickShare.Features.Deliveries.Tag;

public record TagDeliveryCommand(Guid DeliveryId, string? Tag) : IRequest<Result>;

/// <summary>
/// Sets or clears a delivery's sender label (issue #321). The color shown in the
/// list is derived from the text, so no palette storage is needed.
/// </summary>
public sealed class TagDeliveryHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<TagDeliveryCommand, Result>
{
    public async ValueTask<Result> Handle(TagDeliveryCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure(new LightResults.Error(ErrorCodes.NotFound));

        var tag = cmd.Tag?.Trim();
        if (tag is { Length: 0 })
            tag = null;

        if (tag is { Length: > Delivery.TagMaxLength })
            return Result.Failure(new LightResults.Error(ErrorCodes.TagTooLong));

        delivery.Tag = tag;
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class TagDeliveryEndpoint : Endpoint<TagDeliveryRequest>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/tag");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Set or clear a delivery's label";
            s.Description = "Sender only (issue #321). Pass an empty tag to clear.";
        });
    }

    public override async Task HandleAsync(TagDeliveryRequest req, CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result>(
            new TagDeliveryCommand(id, req.Tag), ct);

        if (result.IsSuccess())
            await Send.NoContentAsync(ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, error.Message switch
            {
                ErrorCodes.NotFound => 404,
                _ => 400
            });
    }
}

public class TagDeliveryRequest
{
    public string? Tag { get; set; }
}
