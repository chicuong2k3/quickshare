using QuickShare.Features.Payments.Crypto;
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
    CryptoCheckoutService crypto,
    QuickShare.Features.Payments.Promos.PromoCodeService promos)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeRandomLength = 6;
    private const string CodePrefix = "QSC-";

    public async Task<(PaymentIntent Intent, PayOSPaymentResult Result)> CreatePaymentLinkAsync(
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null)
    {
        var intent = await CreateIntentAsync(user, plan, isYearly, PaymentProviders.PayOS, promoCode, ct);
        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";
        var result = await payOS.CreatePaymentLinkAsync(intent.PayOSOrderCode, (int)intent.Amount, description, ct);

        return (intent, result);
    }

    /// <summary>
    /// The same intent, taken on the crypto rail instead (issue #225).
    ///
    /// Identical price, plan, proration and promo handling — the only difference is where the
    /// buyer is sent and what tells us they paid. A second provider that computed its own prices
    /// would be a second product to keep in step.
    /// </summary>
    public async Task<(PaymentIntent Intent, CryptoChargeResult Result)> CreateCryptoCheckoutAsync(
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null)
    {
        var intent = await CreateIntentAsync(user, plan, isYearly, PaymentProviders.Crypto, promoCode, ct);
        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";

        var result = await crypto.CreateChargeAsync(intent.Code, intent.Amount, intent.Currency, description, ct);

        if (result.Success)
        {
            intent.ProviderChargeId = result.ChargeId;
            await db.SaveChangesAsync(ct);
        }

        return (intent, result);
    }

    /// <summary>Whether the crypto rail is offered at all — no key configured, no button.</summary>
    public bool CryptoAvailable => crypto.IsConfigured;

    private async Task<PaymentIntent> CreateIntentAsync(
        User user, string plan, bool isYearly, string provider, string? promoCode, CancellationToken ct)
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
            Provider = provider,
            Status = PaymentIntentStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync(ct);

        return intent;
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

        return await CompleteAsync(intent, orderCode.ToString(CultureInfo.InvariantCulture), ct);
    }

    /// <summary>
    /// Completes the pending intent behind a confirmed crypto charge (issue #225).
    ///
    /// There is no second call to the provider to confirm it: the webhook's HMAC over the raw
    /// body already proved where the message came from, and asking again would only add a way
    /// for a paid customer to be told no because an API was briefly down.
    /// </summary>
    public async Task<bool> FinalizeByChargeIdAsync(string chargeId, CancellationToken ct)
    {
        var intent = await db.PaymentIntents
            .Where(i => i.ProviderChargeId == chargeId)
            .Where(i => i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
            // Already completed by an earlier delivery of the same event, or unknown. A repeat
            // is the normal case: providers retry until they get a 200.
            return await db.PaymentIntents
                .AsNoTracking()
                .AnyAsync(i => i.ProviderChargeId == chargeId && i.Status == PaymentIntentStatus.Completed, ct);
        }

        return await CompleteAsync(intent, chargeId, ct);
    }

    /// <summary>Everything that happens once money has moved, whichever rail moved it.</summary>
    private async Task<bool> CompleteAsync(PaymentIntent intent, string invoiceId, CancellationToken ct)
    {
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
