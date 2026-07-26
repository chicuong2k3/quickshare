namespace QuickShare.Features.Payments.Kofi;

using System.Text.Json.Serialization;

public sealed class KofiWebhookEndpoint : Endpoint<KofiWebhookRequest>
{
    public override void Configure()
    {
        Post("/api/payments/kofi/webhook");
        AllowAnonymous();
        Description(b => b.WithTags("Payments"));
        Summary(s =>
        {
            s.Summary = "Receive Ko-fi payment webhook";
            s.Description = "Handles Ko-fi subscription events (created, renewed, cancelled, expired). Validates verification token, updates User.Plan, and stores payment history.";
            s.Responses[200] = "Webhook processed or ignored.";
            s.Responses[401] = "Invalid verification token.";
        });
    }

    public override async Task HandleAsync(KofiWebhookRequest req, CancellationToken ct)
    {
        var config = Resolve<IConfiguration>();
        var expectedToken = config["Kofi:VerificationToken"];

        if (string.IsNullOrEmpty(expectedToken) || req.VerificationToken != expectedToken)
        {
            ThrowError("Invalid verification token", 401);
            return;
        }

        var db = Resolve<AppDbContext>();
        var logger = Resolve<ILogger<KofiWebhookEndpoint>>();

        if (!req.IsSubscriptionPayment)
        {
            logger.LogInformation("Ko-fi webhook ignored: not a subscription payment (type={Type})", req.Type);
            await Send.OkAsync(new { status = "ignored", reason = "not a subscription payment" }, ct);
            return;
        }

        var eventType = ResolveEventType(req.IsFirstSubscriptionPayment, req.Type);
        if (eventType is null)
        {
            logger.LogInformation("Ko-fi webhook ignored: unhandled type={Type}", req.Type);
            await Send.OkAsync(new { status = "ignored", reason = $"unhandled type: {req.Type}" }, ct);
            return;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        if (user is null)
        {
            logger.LogWarning("Ko-fi webhook: no user found for email={Email}", req.Email);
            await Send.OkAsync(new { status = "ignored", reason = "no matching user" }, ct);
            return;
        }

        if (eventType is "subscription_cancelled")
        {
            logger.LogInformation("Ko-fi subscription cancelled for user={UserId} plan={Plan} — graceful, no plan change", user.Id, user.Plan);
            db.PaymentEvents.Add(BuildPaymentEvent(user.Id, eventType, req));
            await db.SaveChangesAsync(ct);
            await Send.OkAsync(new { status = "cancelled", userId = user.Id }, ct);
            return;
        }

        if (eventType is "subscription_expired")
        {
            user.Plan = "free";
            logger.LogInformation("Ko-fi subscription expired for user={UserId} — plan set to free", user.Id);
        }
        else
        {
            var plan = ResolvePlanFromTier(req.TierName);
            user.Plan = plan;
            logger.LogInformation("Ko-fi subscription {EventType} for user={UserId} — plan set to {Plan}", eventType, user.Id, plan);
        }

        db.PaymentEvents.Add(BuildPaymentEvent(user.Id, eventType, req));
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new { status = "processed", userId = user.Id, plan = user.Plan }, ct);
    }

    private static string? ResolveEventType(bool isFirst, string type)
    {
        if (isFirst)
            return "subscription_created";

        return type.ToLowerInvariant() switch
        {
            "subscription_renewed" => "subscription_renewed",
            "subscription_cancelled" => "subscription_cancelled",
            "subscription_expired" => "subscription_expired",
            _ => null
        };
    }

    private static string ResolvePlanFromTier(string tierName)
    {
        var lower = tierName.ToLowerInvariant();
        if (lower.Contains("studio"))
            return "studio";
        return "pro";
    }

    private PaymentEvent BuildPaymentEvent(Guid userId, string eventType, KofiWebhookRequest req)
    {
        var plan = ResolvePlanFromTier(req.TierName);
        var planDisplay = plan == "studio" ? "Studio" : "Pro";
        var billing = req.IsRecurringYearly ? "yearly" : "monthly";

        var amount = 0m;
        if (!string.IsNullOrEmpty(req.Amount))
            decimal.TryParse(req.Amount, out amount);

        return new PaymentEvent
        {
            UserId = userId,
            EventType = eventType,
            Plan = plan,
            PlanDisplay = planDisplay,
            Billing = billing,
            Amount = amount,
            Currency = req.Currency,
            InvoiceId = req.KofiTransactionId,
            InvoiceUrl = req.InvoiceUrl,
        };
    }
}

public class KofiWebhookRequest
{
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("verification_token")]
    public string VerificationToken { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;

    public string Currency { get; set; } = "USD";

    [JsonPropertyName("kofi_transaction_id")]
    public string KofiTransactionId { get; set; } = string.Empty;

    [JsonPropertyName("tier_name")]
    public string TierName { get; set; } = string.Empty;

    [JsonPropertyName("is_subscription_payment")]
    public bool IsSubscriptionPayment { get; set; }

    [JsonPropertyName("is_first_subscription_payment")]
    public bool IsFirstSubscriptionPayment { get; set; }

    [JsonPropertyName("is_recurring_yearly")]
    public bool IsRecurringYearly { get; set; }

    [JsonPropertyName("invoice_url")]
    public string InvoiceUrl { get; set; } = string.Empty;
}
