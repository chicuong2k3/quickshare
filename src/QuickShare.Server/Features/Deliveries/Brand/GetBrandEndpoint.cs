namespace QuickShare.Server.Features.Deliveries.Brand;

public sealed class GetBrandEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/deliveries/{ShareCode}/brand");
        AllowAnonymous();
        Description(b => b.WithTags("Deliveries"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<BrandResponse>>(new GetBrandQuery(shareCode), ct);
        if (result.IsSuccess(out var v)) await Send.OkAsync(v, ct);
        else ThrowError("not_found", 404);
    }
}
