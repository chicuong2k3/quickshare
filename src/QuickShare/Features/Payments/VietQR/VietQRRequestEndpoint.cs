namespace QuickShare.Features.Payments.VietQR;

public sealed class VietQRRequestEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/payment/vietqr");
        AllowAnonymous();
        Description(b => b.WithTags("Deliveries"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode")!;
        var db = Resolve<AppDbContext>();
        var config = Resolve<IConfiguration>();
        var logger = Resolve<ILogger<VietQRRequestEndpoint>>();

        var delivery = await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.ShareCode == shareCode, ct);
        if (delivery is null) { ThrowError("Not found", 404); return; }
        if (!delivery.Price.HasValue || delivery.Price.Value <= 0)
            ThrowError("No payment required", 400);

        var bankId = config["VietQR:BankId"] ?? "970422";
        var bankAccount = config["VietQR:BankAccount"] ?? "0000000000";
        var bankName = config["VietQR:BankName"] ?? "VietQR Bank";
        var amount = (long)(delivery.Price.Value * 1000);
        var note = $"QSC-{delivery.ShareCode[..Math.Min(8, delivery.ShareCode.Length)]}";
        var qrDataUrl = $"https://api.vietqr.io/image/{bankId}-{bankAccount}-{amount}-{note}.jpg?accountName=QuickShareCloud";

        logger.LogInformation("Generated VietQR for delivery {ShareCode}: amount={Amount} VND", shareCode, amount);

        await Send.OkAsync(new
        {
            qrDataUrl,
            amount,
            bankInfo = bankName,
            bankAccount,
            expiresAt = DateTime.UtcNow.AddMinutes(15)
        }, ct);
    }
}
