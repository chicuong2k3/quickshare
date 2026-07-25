namespace QuickShare.Server.Features.Payments.VietQR;

public sealed class VietQRWebhookEndpoint : Endpoint<VietQRWebhookRequest>
{
    public override void Configure()
    {
        Post("/api/payments/vietqr/webhook");
        AllowAnonymous();
        Description(b => b.WithTags("Payments"));
    }

    public override async Task HandleAsync(VietQRWebhookRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var logger = Resolve<ILogger<VietQRWebhookEndpoint>>();

        var delivery = await db.Deliveries.FirstOrDefaultAsync(d => d.ShareCode == req.ShareCode, ct);
        if (delivery is null)
        {
            logger.LogWarning("VietQR webhook received for unknown share code: {ShareCode}", req.ShareCode);
            await Send.OkAsync(new { status = "ignored" }, ct);
            return;
        }

        delivery.PaymentStatus = "Paid";
        await db.SaveChangesAsync(ct);

        logger.LogInformation("VietQR payment confirmed for delivery {ShareCode}", req.ShareCode);
        await Send.OkAsync(new { status = "paid" }, ct);
    }
}

public class VietQRWebhookRequest
{
    public string ShareCode { get; set; } = "";
    public string TransactionId { get; set; } = "";
    public long Amount { get; set; }
}
