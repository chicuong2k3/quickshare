namespace QuickShare.Server.Features.Payments;

public sealed class KofiWebhookEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/payments/ko-fi/webhook");
        AllowAnonymous();
        AllowFormData();
        Description(b => b.WithTags("Payments"));
        Summary(s => { s.Summary = "Ko-fi payment webhook"; s.Responses[200] = "Payment verified."; });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var form = await HttpContext.Request.ReadFormAsync(ct);
        var data = form["data"].FirstOrDefault();
        if (string.IsNullOrEmpty(data)) { ThrowError("Missing webhook data", 400); return; }

        var parsed = System.Text.Json.JsonSerializer.Deserialize<KofiPayload>(data);
        if (parsed is null || string.IsNullOrEmpty(parsed.Message)) { ThrowError("Invalid payload", 400); return; }

        // Extract delivery ID from message (format: "delivery:{guid}")
        if (!parsed.Message.StartsWith("delivery:") || !Guid.TryParse(parsed.Message[9..], out var deliveryId))
        { ThrowError("Invalid delivery reference", 400); return; }

        var db = Resolve<AppDbContext>();
        var delivery = await db.Deliveries.FindAsync([deliveryId], ct);
        if (delivery is not null)
        {
            delivery.PaymentStatus = "paid";
            await db.SaveChangesAsync(ct);
        }
        await Send.OkAsync(new { status = "verified" }, ct);
    }

    private sealed class KofiPayload { public string? Message { get; set; } public string? Type { get; set; } }
}
