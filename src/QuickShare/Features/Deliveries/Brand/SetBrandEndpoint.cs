namespace QuickShare.Features.Deliveries.Brand;

public sealed class SetBrandEndpoint : Endpoint<SetBrandRequest>
{
    public override void Configure()
    {
        Put("/api/deliveries/{DeliveryId}/brand");
        Description(b => b.WithTags(ApiTags.Deliveries));
    }

    public override async Task HandleAsync(SetBrandRequest req, CancellationToken ct)
    {
        var deliveryId = Route<Guid>("DeliveryId");
        var cmd = new SetBrandCommand(deliveryId, req.Logo, req.Message, req.Accent);
        var result = await Resolve<IMessageBus>().InvokeAsync<LightResults.Result<BrandResponse>>(cmd, ct);
        if (result.IsSuccess(out var v)) await Send.OkAsync(v, ct);
        else if (result.IsFailure(out var e)) ThrowError(e.Message, 403);
    }
}

public class SetBrandRequest { public string? Logo { get; set; } public string? Message { get; set; } public string? Accent { get; set; } }
