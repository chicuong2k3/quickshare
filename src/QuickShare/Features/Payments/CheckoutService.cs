using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Payments.Subscriptions;

namespace QuickShare.Features.Payments;

/// <summary>
/// Money-path logic extracted from Upgrade.razor: creating a PaymentIntent + PayOS
/// checkout link, and finalizing a successful PayOS redirect callback (idempotent).
/// </summary>
public sealed class CheckoutService(AppDbContext db, IConfiguration config, PayOSService payOS)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeRandomLength = 6;
    private const string CodePrefix = "QSC-";

    public async Task<(PaymentIntent Intent, PayOSPaymentResult Result)> CreatePaymentLinkAsync(
        User user, string plan, bool isYearly, CancellationToken ct)
    {
        plan = PlanCatalog.Normalize(plan);
        var pricingKey = PlanCatalog.PricingKey(plan, isYearly);
        var expectedAmount = config.GetValue(pricingKey, PlanCatalog.DefaultAmount(plan, isYearly));
        var currency = config[PricingKeys.Currency] ?? PricingKeys.DefaultCurrency;

        var code = await GenerateUniqueCodeAsync(db, ct);
        var payOSOrderCode = PayOSService.GenerateOrderCode();

        while (await db.PaymentIntents.AnyAsync(
            i => i.PayOSOrderCode == payOSOrderCode && i.Status == PaymentIntentStatus.Pending,
            ct))
        {
            payOSOrderCode = PayOSService.GenerateOrderCode();
        }

        var intent = new PaymentIntent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Code = code,
            PayOSOrderCode = payOSOrderCode,
            Plan = plan,
            Billing = isYearly ? BillingPeriods.Yearly : BillingPeriods.Monthly,
            Amount = expectedAmount,
            Currency = currency,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync(ct);

        var description = $"{CodePrefix}{code[^CodeRandomLength..]}";
        var result = await payOS.CreatePaymentLinkAsync(payOSOrderCode, expectedAmount, description, ct);
        return (intent, result);
    }

    /// <summary>Completes the pending intent for a successful PayOS redirect. Safe to call twice.</summary>
    public async Task TryFinalizeCallbackAsync(User user, long orderCode, CancellationToken ct)
    {
        var intent = await db.PaymentIntents
            .Where(i => i.UserId == user.Id)
            .Where(i => i.PayOSOrderCode == orderCode)
            .Where(i => i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
            return;
        }

        await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);

        var invoiceId = orderCode.ToString();
        var hasExistingPaymentEvent = await db.PaymentEvents
            .Where(x => x.UserId == user.Id)
            .Where(x => x.EventType == PaymentEventTypes.Payment)
            .Where(x => x.InvoiceId == invoiceId)
            .AnyAsync(ct);

        if (!hasExistingPaymentEvent)
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

        intent.Status = PaymentIntentStatus.Completed;
        intent.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static async Task<string> GenerateUniqueCodeAsync(AppDbContext db, CancellationToken ct)
    {
        var random = Random.Shared;

        while (true)
        {
            var code = CodePrefix + new string(Enumerable.Range(0, CodeRandomLength)
                .Select(_ => CodeAlphabet[random.Next(CodeAlphabet.Length)])
                .ToArray());

            var exists = await db.PaymentIntents
                .AnyAsync(i => i.Code == code && i.Status == PaymentIntentStatus.Pending, ct);

            if (!exists)
                return code;
        }
    }
}
