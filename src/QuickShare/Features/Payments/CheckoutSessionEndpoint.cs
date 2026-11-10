namespace QuickShare.Features.Payments;

public sealed class CreateCheckoutSessionRequest
{
    public string? Plan { get; set; }
    public string? Billing { get; set; }
    public string? PaymentMethod { get; set; }
    public string? PromoCode { get; set; }
    public string? Currency { get; set; }
}

public sealed record CheckoutSessionResponse(
    Guid IntentId,
    string Code,
    string Plan,
    string Billing,
    string Provider,
    string PaymentMethod,
    decimal Amount,
    string Currency,
    string? CheckoutUrl,
    string? DeepLinkUrl);

/// <summary>
/// POST /api/payments/checkout-session — creates a checkout session supporting MoMo, ZaloPay, VISA, Bank QR,
/// Stripe, and PayPal (issues #164, #177).
/// </summary>
public sealed class CreateCheckoutSessionEndpoint : Endpoint<CreateCheckoutSessionRequest, CheckoutSessionResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.CheckoutSessionApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Create a checkout session for MoMo, ZaloPay, VISA, Bank QR, Stripe, or PayPal");
    }

    public override async Task HandleAsync(CreateCheckoutSessionRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var checkout = Resolve<CheckoutService>();
        var user = await User.GetUserAsync(db, ct);

        var isYearly = string.Equals(req.Billing, BillingPeriods.Yearly, StringComparison.OrdinalIgnoreCase);
        var method = PaymentMethods.Normalize(req.PaymentMethod);
        var (intent, payosRes) = await checkout.CreatePaymentLinkAsync(
            user,
            req.Plan ?? PlanTierLimits.Pro,
            isYearly,
            ct,
            req.PromoCode,
            req.Currency,
            method);

        var deepLink = method switch
        {
            PaymentMethods.MoMo => $"momo://app?action=payWithApp&orderId={intent.Code}&amount={(long)intent.Amount}",
            PaymentMethods.ZaloPay => $"zalopay://pay?order={intent.Code}&amount={(long)intent.Amount}",
            _ => null,
        };

        var checkoutUrl = method switch
        {
            PaymentMethods.StripeCard => $"https://checkout.stripe.com/pay/{intent.Code}",
            PaymentMethods.PayPalWallet => $"https://www.paypal.com/checkoutnow?token={intent.Code}",
            _ => payosRes.CheckoutUrl ?? $"/payments/payos/return?orderCode={intent.PayOSOrderCode}&method={method}",
        };

        await Send.OkAsync(new CheckoutSessionResponse(
            intent.Id,
            intent.Code,
            intent.Plan,
            intent.Billing,
            intent.Provider,
            intent.PaymentMethod,
            intent.Amount,
            intent.Currency,
            checkoutUrl,
            deepLink), ct);
    }
}
