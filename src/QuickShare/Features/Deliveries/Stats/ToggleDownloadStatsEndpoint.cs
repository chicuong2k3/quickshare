namespace QuickShare.Features.Deliveries.Stats;

public sealed class ToggleDownloadStatsEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Patch("/api/deliveries/{Id}/download-stats");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Toggle public download stats on a delivery";
            s.Description = "Sender only. When hidden, the recipient page shows no aggregate download count or first-download time.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = Route<Guid>("Id");
        var result = await Resolve<IMediator>().Send<LightResults.Result<bool>>(
            new ToggleDownloadStatsCommand(id), ct);

        if (result.IsSuccess(out var hidden))
            await Send.OkAsync(new { hidden }, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, 404);
    }
}
