namespace QuickShare.Features.Deliveries.Favorite;

public record ToggleFavoriteResponse(bool IsFavorite);

public sealed class ToggleFavoriteEndpoint : EndpointWithoutRequest<ToggleFavoriteResponse>
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/favorite");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Toggle the sender-side star on a delivery";
            s.Description = "Sender only (issue #242). Server-stored, so favorites follow the account across devices.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result<ToggleFavoriteResponse>>(
            new ToggleFavoriteCommand(id), ct);

        if (result.IsSuccess(out var value))
            await Send.OkAsync(value, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 404);
    }
}
