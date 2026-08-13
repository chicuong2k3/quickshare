using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;

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
