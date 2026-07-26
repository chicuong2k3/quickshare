using System.Text.Json.Serialization;
using QuickShare.Features.Payments.Subscriptions;

namespace QuickShare.Features.Payments.Kofi;

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
            s.Description = "Records all Ko-fi payments. For plan upgrades, matches the user by the payment code " +
                "in the message field (included when user pays from the upgrade page).";
            s.Responses[200] = "Webhook processed.";
            s.Responses[401] = "Invalid verification token.";
        });
    }

    public override async Task HandleAsync(KofiWebhookRequest req, CancellationToken ct)
    {
        var config = Resolve<IConfiguration>();
        var expectedToken = config["Kofi:VerificationToken"];
        var logger = Resolve<ILogger<KofiWebhookEndpoint>>();

        if (string.IsNullOrEmpty(expectedToken) || req.VerificationToken != expectedToken)
        {
            ThrowError("Invalid verification token", 401);
            return;
        }

        var db = Resolve<AppDbContext>();

        var amount = 0m;
        if (!string.IsNullOrEmpty(req.Amount))
            decimal.TryParse(req.Amount, out amount);

        var resolved = ResolvePlanFromAmount(amount, config);

        // Try to match user by payment code in the message field.
        User? user = null;
        PaymentIntent? intent = null;

        var code = ExtractPaymentCode(req.Message);
        if (code is not null)
        {
            intent = await db.PaymentIntents
                .Where(i => i.Code == code && i.Status == PaymentIntentStatus.Pending)
                .FirstOrDefaultAsync(ct);

            if (intent is not null)
            {
                user = await db.Users.FindAsync(new object[] { intent.UserId }, ct);
            }
        }

        // Fall back to email matching if no code match.
        if (user is null && !string.IsNullOrEmpty(req.Email))
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Email == req.Email, ct);
        }

        // Always record the payment event.
        var paymentEvent = BuildPaymentEvent(user?.Id, req, resolved);
        db.PaymentEvents.Add(paymentEvent);

        if (resolved is not null && user is not null)
        {
            var (plan, billing) = resolved.Value;

            if (req.Type is "Subscription" or "subscription_renewed")
            {
                await SubscriptionService.HandlePaymentAsync(user, plan, billing, db, ct);

                if (intent is not null)
                {
                    intent.Status = PaymentIntentStatus.Completed;
                    intent.CompletedAt = DateTime.UtcNow;
                }

                logger.LogInformation(
                    "Payment processed: user={UserId} plan={Plan} billing={Billing} amount={Amount} {Currency} matchedBy={Method}",
                    user.Id, plan, billing, amount, req.Currency,
                    intent is not null ? "code" : "email");
            }
            else
            {
                logger.LogInformation(
                    "Ko-fi event recorded (no plan change): user={UserId} type={Type}",
                    user.Id, req.Type);
            }
        }
        else if (user is null)
        {
            logger.LogWarning(
                "Payment recorded without user link: email={Email} amount={Amount} {Currency} message={Message}",
                req.Email, amount, req.Currency, req.Message);
        }
        else
        {
            logger.LogWarning(
                "Payment amount={Amount} {Currency} does not match any plan pricing — recorded without plan activation",
                amount, req.Currency);
        }

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(new { status = "processed" }, ct);
    }

    private static string? ExtractPaymentCode(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        // Codes are uppercase alphanumeric prefixed with "QSC-".
        var idx = message.IndexOf("QSC-", StringComparison.Ordinal);
        if (idx < 0)
            return null;

        var candidate = message.AsSpan(idx, Math.Min(10, message.Length - idx));
        return candidate.ToString();
    }

    private static (string plan, string billing)? ResolvePlanFromAmount(decimal amount, IConfiguration config)
    {
        var pricing = config.GetSection("Pricing");

        if (amount == pricing.GetValue<int>("ProMonthly"))   return ("pro", "monthly");
        if (amount == pricing.GetValue<int>("ProYearly"))    return ("pro", "yearly");
        if (amount == pricing.GetValue<int>("StudioMonthly")) return ("studio", "monthly");
        if (amount == pricing.GetValue<int>("StudioYearly")) return ("studio", "yearly");

        return null;
    }

    private PaymentEvent BuildPaymentEvent(Guid? userId, KofiWebhookRequest req,
        (string plan, string billing)? resolved)
    {
        var plan = resolved?.plan ?? "";
        var planDisplay = plan switch
        {
            "studio" => "Studio",
            "pro" => "Pro",
            _ => ""
        };
        var billing = resolved?.billing ?? "";

        var amount = 0m;
        if (!string.IsNullOrEmpty(req.Amount))
            decimal.TryParse(req.Amount, out amount);

        return new PaymentEvent
        {
            UserId = userId ?? Guid.Empty,
            EventType = NormalizeEventType(req.Type),
            Plan = plan,
            PlanDisplay = planDisplay,
            Billing = billing,
            Amount = amount,
            Currency = req.Currency,
            InvoiceId = req.KofiTransactionId,
            InvoiceUrl = req.InvoiceUrl,
        };
    }

    private static string NormalizeEventType(string type)
    {
        return type.ToLowerInvariant() switch
        {
            "subscription" => "subscription_created",
            "subscription_renewed" => "subscription_renewed",
            "subscription_cancelled" => "subscription_cancelled",
            "subscription_expired" => "subscription_expired",
            "tip" => "tip",
            "commission" => "commission",
            "shop_order" => "shop_order",
            _ => type.ToLowerInvariant()
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

    public string Message { get; set; } = string.Empty;

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
