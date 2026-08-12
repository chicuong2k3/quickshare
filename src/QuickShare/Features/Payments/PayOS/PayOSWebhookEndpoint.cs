using System.Text.Json.Nodes;
using PayOS;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments.PayOS;

/// <summary>
/// Receives and verifies PayOS payment webhooks.
/// POST /api/payments/payos/webhook
/// </summary>
public sealed class PayOSWebhookEndpoint : EndpointWithoutRequest
{
    private const string DataKey = "data";
    private const string SignatureKey = "signature";
    private const string OrderCodeKey = "orderCode";
    private const string ReferenceKey = "reference";
    private const string PaymentLinkIdKey = "paymentLinkId";

    public override void Configure()
    {
        Post("/api/payments/payos/webhook");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Receive PayOS payment webhook";
            s.Description = "Verifies PayOS webhook signature, matches the PaymentIntent by orderCode, " +
                "and activates the corresponding subscription.";
            s.Responses[200] = "Webhook processed.";
            s.Responses[401] = "Invalid signature.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var config = Resolve<IConfiguration>();
        var logger = Resolve<ILogger<PayOSWebhookEndpoint>>();

        // Verify the signature over the RAW `data` object exactly as PayOS sent it.
        // Deserializing into a typed model first drops fields PayOS signed
        // (counterAccount*, virtualAccount*, ...) and every real webhook then fails
        // verification — which is why paid upgrades never activated.
        JsonNode? root;
        try
        {
            root = await JsonNode.ParseAsync(HttpContext.Request.Body, cancellationToken: ct);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "PayOS webhook: malformed JSON body");
            ThrowError("Invalid webhook payload", 400);
            return;
        }

        var data = root?[DataKey];
        var signature = root?[SignatureKey]?.GetValue<string>();

        if (data is null || string.IsNullOrEmpty(signature))
        {
            logger.LogWarning("PayOS webhook: missing data or signature");
            ThrowError("Invalid webhook payload", 400);
            return;
        }

        var checksumKey = config["PayOS:ChecksumKey"];
        if (string.IsNullOrEmpty(checksumKey))
        {
            logger.LogError("PayOS:ChecksumKey is not configured");
            ThrowError("PayOS configuration error", 500);
            return;
        }

        var client = new PayOSClient(config["PayOS:ClientId"]!, config["PayOS:ApiKey"]!, checksumKey);
        var expected = client.Crypto.CreateSignatureFromObject(data, checksumKey);

        if (!string.Equals(expected, signature, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("PayOS webhook signature verification failed");
            ThrowError("Invalid webhook signature", 401);
            return;
        }

        var db = Resolve<AppDbContext>();
        var orderCode = data[OrderCodeKey]?.GetValue<long>() ?? 0;

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

        var invoiceId = data[ReferenceKey]?.GetValue<string>()
            ?? data[PaymentLinkIdKey]?.GetValue<string>()
            ?? orderCode.ToString();

        // Record payment event (idempotent — PayOS retries webhooks)
        var alreadyRecorded = await db.PaymentEvents
            .Where(e => e.UserId == user.Id)
            .Where(e => e.EventType == PaymentEventTypes.Payment)
            .Where(e => e.InvoiceId == invoiceId)
            .AnyAsync(ct);

        if (!alreadyRecorded)
        {
            db.PaymentEvents.Add(new PaymentEvent
            {
                UserId = user.Id,
                EventType = PaymentEventTypes.Payment,
                Plan = intent.Plan,
                PlanDisplay = PlanCatalog.Get(intent.Plan).Display,
                Billing = intent.Billing,
                Amount = intent.Amount,
                Currency = intent.Currency,
                InvoiceId = invoiceId,
                InvoiceUrl = string.Empty
            });
        }

        // Activate subscription
        await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);

        // Mark PaymentIntent as completed
        intent.Status = PaymentIntentStatus.Completed;
        intent.CompletedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // Evict cached user profile/plan/billing-history entries so the UI
        // immediately reflects the new plan instead of the stale cached copy
        // (the webhook user instance came from FindAsync, not GetUserAsync).
        await UserCacheInvalidator.EvictAsync(db, user.Id, ct);

        logger.LogInformation(
            "PayOS payment processed: user={UserId} plan={Plan} billing={Billing} amount={Amount} {Currency} orderCode={OrderCode}",
            user.Id, intent.Plan, intent.Billing, intent.Amount, intent.Currency, orderCode);

        await Send.OkAsync(new { status = "processed" }, ct);
    }
}
