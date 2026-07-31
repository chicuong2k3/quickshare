using PayOS;
using PayOS.Exceptions;
using PayOS.Models.Webhooks;
using QuickShare.Features.Payments.Subscriptions;

namespace QuickShare.Features.Payments.PayOS;

/// <summary>
/// Receives and verifies PayOS payment webhooks.
/// POST /api/payments/payos/webhook
/// </summary>
public sealed class PayOSWebhookEndpoint : Endpoint<PayOSWebhookData>
{
    public override void Configure()
    {
        Post("/api/payments/payos/webhook");
        AllowAnonymous();
        Description(b => b.WithTags("Payments"));
        Summary(s =>
        {
            s.Summary = "Receive PayOS payment webhook";
            s.Description = "Verifies PayOS webhook signature, matches the PaymentIntent by orderCode, " +
                "and activates the corresponding subscription.";
            s.Responses[200] = "Webhook processed.";
            s.Responses[401] = "Invalid signature.";
        });
    }

    public override async Task HandleAsync(PayOSWebhookData req, CancellationToken ct)
    {
        var config = Resolve<IConfiguration>();
        var logger = Resolve<ILogger<PayOSWebhookEndpoint>>();

        // Build PayOS client
        PayOSClient client;
        try
        {
            var clientId = config["PayOS:ClientId"]!;
            var apiKey = config["PayOS:ApiKey"]!;
            var checksumKey = config["PayOS:ChecksumKey"]!;
            client = new PayOSClient(clientId, apiKey, checksumKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PayOS client configuration failed");
            ThrowError("PayOS configuration error", 500);
            return;
        }

        // Verify webhook signature
        WebhookData verified;
        try
        {
            var webhook = new Webhook
            {
                Code = req.Code,
                Description = req.Description,
                Success = req.Success,
                Data = new WebhookData
                {
                    OrderCode = req.Data.OrderCode,
                    Amount = req.Data.Amount,
                    Description = req.Data.Description ?? "",
                    AccountNumber = req.Data.AccountNumber ?? "",
                    Reference = req.Data.Reference ?? "",
                    TransactionDateTime = req.Data.TransactionDateTime ?? "",
                    Currency = req.Data.Currency ?? "VND",
                    PaymentLinkId = req.Data.PaymentLinkId ?? "",
                    Code = req.Data.Code ?? "",
                    Description2 = req.Data.Description2 ?? ""
                },
                Signature = req.Signature
            };

            verified = await client.Webhooks.VerifyAsync(webhook);
        }
        catch (WebhookException ex)
        {
            logger.LogWarning(ex, "PayOS webhook signature verification failed");
            ThrowError("Invalid webhook signature", 401);
            return;
        }

        var db = Resolve<AppDbContext>();
        var orderCode = verified.OrderCode;

        // Look up PaymentIntent by PayOS order code
        var intent = await db.PaymentIntents
            .Where(i => i.PayOSOrderCode == orderCode && i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
            logger.LogWarning(
                "PayOS webhook: no pending PaymentIntent found for orderCode={OrderCode}",
                orderCode);
            // Payment recorded but can't match to a user — still return 200 to acknowledge webhook
            await Send.OkAsync(new { status = "ignored", reason = "no matching intent" }, ct);
            return;
        }

        // Find user
        var user = await db.Users.FindAsync(new object[] { intent.UserId }, ct);
        if (user is null)
        {
            logger.LogError(
                "PayOS webhook: PaymentIntent {IntentId} has invalid UserId={UserId}",
                intent.Id, intent.UserId);
            await Send.OkAsync(new { status = "ignored", reason = "invalid user" }, ct);
            return;
        }

        // Record payment event
        var paymentEvent = new PaymentEvent
        {
            UserId = user.Id,
            EventType = "payment",
            Plan = intent.Plan,
            PlanDisplay = intent.Plan switch
            {
                "studio" => "Studio",
                "pro" => "Pro",
                _ => ""
            },
            Billing = intent.Billing,
            Amount = intent.Amount,
            Currency = intent.Currency,
            InvoiceId = verified.Reference ?? verified.PaymentLinkId,
            InvoiceUrl = ""
        };
        db.PaymentEvents.Add(paymentEvent);

        // Activate subscription
        await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);

        // Mark PaymentIntent as completed
        intent.Status = PaymentIntentStatus.Completed;
        intent.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "PayOS payment processed: user={UserId} plan={Plan} billing={Billing} amount={Amount} {Currency} orderCode={OrderCode}",
            user.Id, intent.Plan, intent.Billing, intent.Amount, intent.Currency, orderCode);

        await Send.OkAsync(new { status = "processed" }, ct);
    }
}

/// <summary>
/// Minimal model for PayOS webhook JSON body.
/// Maps to PayOS's { code, desc, success, data, signature } structure.
/// </summary>
public sealed class PayOSWebhookData
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public bool Success { get; set; }
    public PayOSWebhookDataPayload Data { get; set; } = new();
    public string Signature { get; set; } = "";
}

public sealed class PayOSWebhookDataPayload
{
    public long OrderCode { get; set; }
    public long Amount { get; set; }
    public string? Description { get; set; }
    public string? AccountNumber { get; set; }
    public string? Reference { get; set; }
    public string? TransactionDateTime { get; set; }
    public string? Currency { get; set; }
    public string? PaymentLinkId { get; set; }
    public string? Code { get; set; }
    public string? Description2 { get; set; }
}
