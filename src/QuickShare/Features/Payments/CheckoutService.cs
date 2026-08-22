using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments;

/// <summary>
/// Money-path logic extracted from Upgrade.razor: creating a PaymentIntent + PayOS
/// checkout link, and finalizing a successful PayOS redirect callback (idempotent).
/// </summary>
public sealed class CheckoutService(
    AppDbContext db,
    IConfiguration config,
    PayOSService payOS,
    QuickShare.Features.Payments.Promos.PromoCodeService promos)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeRandomLength = 6;
    private const string CodePrefix = "QSC-";

    public async Task<(PaymentIntent Intent, PayOSPaymentResult Result)> CreatePaymentLinkAsync(
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null)
    {
        plan = PlanCatalog.Normalize(plan);
        var pricingKey = PlanCatalog.PricingKey(plan, isYearly);
        var listAmount = config.GetValue(pricingKey, PlanCatalog.DefaultAmount(plan, isYearly));
        var currency = config[PricingKeys.Currency] ?? PricingKeys.DefaultCurrency;

        // ── Proration (issue #114) ──
        // An upgrade mid-cycle should not make somebody pay twice for the days they already
        // bought. Without this, upgrading is a penalty — which is why people wait for renewal,
        // and some of them never come back to it.
        var current = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        var credit = 0;
        if (current is not null && Subscriptions.SubscriptionLifecycle.IsUpgrade(current.Plan, plan))
        {
            var paidKey = PlanCatalog.PricingKey(current.Plan, current.BillingPeriod == BillingPeriod.Yearly);
            var paidAmount = config.GetValue(
                paidKey, PlanCatalog.DefaultAmount(current.Plan, current.BillingPeriod == BillingPeriod.Yearly));

            credit = Subscriptions.SubscriptionLifecycle.ProrationCredit(
                current, paidAmount, listAmount, DateTime.UtcNow);
        }

        // ── Promo code (issue #98) ──
        // Quoted, not redeemed: redemption happens when the payment actually completes, so an
        // abandoned checkout does not burn a limited code.
        var afterCredit = Math.Max(listAmount - credit, 0);
        var quote = await promos.QuoteAsync(promoCode, user.Id, plan, afterCredit, ct);
        var expectedAmount = quote.Valid ? quote.FinalAmount : afterCredit;

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
            // Kept on the intent so the completing side knows which code to consume, and so a
            // discounted payment can be reconciled against a list price later.
            PromoCode = quote.Valid ? PromoCode.NormalizeCode(promoCode) : null,
            ProrationCredit = credit,
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

    /// <summary>True once the webhook (or the redirect callback) has completed this order.</summary>
    public Task<bool> IsCompletedAsync(User user, long orderCode, CancellationToken ct) =>
        db.PaymentIntents
            .AsNoTracking()
            .AnyAsync(i =>
                i.UserId == user.Id &&
                i.PayOSOrderCode == orderCode &&
                i.Status == PaymentIntentStatus.Completed, ct);

    /// <summary>
    /// Completes the pending intent for an order PayOS confirms as paid. Safe to call twice,
    /// and independent of who is browsing — the PayOS return URL is loaded inside an iframe
    /// where our auth cookie may not travel, so the intent itself identifies the user.
    /// </summary>
    public async Task<bool> FinalizeByOrderCodeAsync(long orderCode, CancellationToken ct)
    {
        var intent = await db.PaymentIntents
            .Where(i => i.PayOSOrderCode == orderCode)
            .Where(i => i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
            // Already completed by the webhook (or unknown order) — treat a completed one as success.
            return await db.PaymentIntents
                .AsNoTracking()
                .AnyAsync(i => i.PayOSOrderCode == orderCode && i.Status == PaymentIntentStatus.Completed, ct);
        }

        if (!await payOS.IsPaidAsync(orderCode, ct))
        {
            return false;
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == intent.UserId, ct);
        if (user is null)
        {
            return false;
        }

        await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);

        // Consume the code now that money has actually moved (issue #98). Quoting at checkout
        // deliberately did not, so an abandoned checkout leaves a limited code intact.
        //
        // A failure here is not worth failing the payment over: the customer has already been
        // charged the discounted amount by the provider, and refusing to record the redemption
        // would lose them the plan they just paid for over an accounting detail.
        if (!string.IsNullOrEmpty(intent.PromoCode))
        {
            await promos.RedeemAsync(
                intent.PromoCode, user.Id, intent.Plan, (int)intent.Amount + intent.ProrationCredit, ct);
        }

        var invoiceId = orderCode.ToString(CultureInfo.InvariantCulture);
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

        // Evict the cached User entity (plan) and billing-history snapshot so the
        // next page render shows the activated plan immediately.
        await UserCacheInvalidator.EvictAsync(db, user.Id, ct);
        return true;
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
