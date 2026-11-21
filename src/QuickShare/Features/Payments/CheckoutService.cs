using System.Globalization;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments;

/// <summary>
/// Money-path logic extracted from Upgrade.razor: creating a PaymentIntent + PayOS
/// checkout link, finalizing a successful PayOS redirect callback (idempotent), and
/// multi-currency pricing snapshots (issue #120).
///
/// The crypto rail (#225), gift subscriptions (#102) and the MoMo/ZaloPay/Stripe/PayPal
/// checkout-session endpoint (#164, #177) used to live here. All were retired for the same
/// reason: the storefront sells one rail, PayOS, because it is the only one that actually
/// settles in VND. The others minted a payment intent and then handed back a checkout URL
/// or a deeplink nobody had called that provider to create — a link that 404s on a stranger's
/// site is not a payment option, it is a promise the code cannot keep.
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
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null, string? currency = null)
    {
        var intent = await CreateIntentAsync(user, plan, isYearly, promoCode, currency, ct);
        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";
        var result = await payOS.CreatePaymentLinkAsync(intent.PayOSOrderCode, (int)intent.Amount, description, ct);

        return (intent, result);
    }

    private async Task<PaymentIntent> CreateIntentAsync(
        User user, string plan, bool isYearly, string? promoCode, string? requestedCurrency, CancellationToken ct)
    {
        plan = PlanCatalog.Normalize(plan);
        var defaultCurr = config[PricingKeys.Currency] ?? PricingKeys.DefaultCurrency;
        var currency = string.IsNullOrWhiteSpace(requestedCurrency)
            ? PlanCatalog.NormalizeCurrency(defaultCurr)
            : PlanCatalog.NormalizeCurrency(requestedCurrency);

        var pricingKey = PlanCatalog.PricingKey(plan, isYearly);
        var listAmount = currency == PricingKeys.DefaultCurrency
            ? config.GetValue(pricingKey, PlanCatalog.DefaultAmount(plan, isYearly, currency))
            : PlanCatalog.DefaultAmount(plan, isYearly, currency);

        // Proration (issue #114): an upgrade is billed for the difference, never the full price.
        var current = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        var credit = 0;
        if (current is not null && Subscriptions.SubscriptionLifecycle.IsUpgrade(current.Plan, plan))
        {
            var paidKey = PlanCatalog.PricingKey(current.Plan, current.BillingPeriod == BillingPeriod.Yearly);
            var paidAmount = currency == PricingKeys.DefaultCurrency
                ? config.GetValue(paidKey, PlanCatalog.DefaultAmount(current.Plan, current.BillingPeriod == BillingPeriod.Yearly, currency))
                : PlanCatalog.DefaultAmount(current.Plan, current.BillingPeriod == BillingPeriod.Yearly, currency);

            credit = Subscriptions.SubscriptionLifecycle.ProrationCredit(
                current, paidAmount, listAmount, DateTime.UtcNow);
        }

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
            ExchangeRateSnapshot = PlanCatalog.ExchangeRateFor(currency),
            PromoCode = quote.Valid ? PromoCode.NormalizeCode(promoCode) : null,
            ProrationCredit = credit,
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

    public async Task<bool> FinalizeByOrderCodeAsync(long orderCode, CancellationToken ct)
    {
        var intent = await db.PaymentIntents
            .Where(i => i.PayOSOrderCode == orderCode)
            .Where(i => i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
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

    /// <summary>Everything that happens once money has moved.</summary>
    public async Task<bool> CompleteAsync(PaymentIntent intent, string invoiceId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == intent.UserId, ct);
        if (user is null)
        {
            return false;
        }

        await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);

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
