namespace QuickShare.Features.Payments;

public sealed class VerifyPaymentEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/deliveries/{ShareCode}/payment");
        AllowAnonymous();
        Description(b => b.WithTags("Deliveries"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var db = Resolve<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.ShareCode == shareCode, ct);
        if (delivery is null) { ThrowError("Not found", 404); return; }
        await Send.OkAsync(new { delivery.Price, delivery.PaymentStatus }, ct);
    }
}
