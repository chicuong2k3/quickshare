using System.Globalization;
using System.Security.Cryptography;
using QuickShare.Features.Payments.Crypto;
using QuickShare.Features.Payments.PayOS;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Payments;

/// <summary>
/// Money-path logic extracted from Upgrade.razor: creating a PaymentIntent + PayOS
/// checkout link, finalizing a successful PayOS redirect callback (idempotent),
/// multi-currency pricing snapshots (issue #120), and gift subscriptions (issue #102).
/// </summary>
public sealed class CheckoutService(
    AppDbContext db,
    IConfiguration config,
    PayOSService payOS,
    CryptoCheckoutService crypto,
    QuickShare.Features.Payments.Promos.PromoCodeService promos,
    EmailSender emailSender)
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeRandomLength = 6;
    private const string CodePrefix = "QSC-";

    public async Task<(PaymentIntent Intent, PayOSPaymentResult Result)> CreatePaymentLinkAsync(
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null, string? currency = null)
    {
        var intent = await CreateIntentAsync(user, plan, isYearly, PaymentProviders.PayOS, promoCode, currency, ct);
        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";
        var result = await payOS.CreatePaymentLinkAsync(intent.PayOSOrderCode, (int)intent.Amount, description, ct);

        return (intent, result);
    }

    /// <summary>
    /// The same intent, taken on the crypto rail instead (issue #225).
    /// </summary>
    public async Task<(PaymentIntent Intent, CryptoChargeResult Result)> CreateCryptoCheckoutAsync(
        User user, string plan, bool isYearly, CancellationToken ct, string? promoCode = null, string? currency = null)
    {
        var intent = await CreateIntentAsync(user, plan, isYearly, PaymentProviders.Crypto, promoCode, currency, ct);
        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";

        var result = await crypto.CreateChargeAsync(intent.Code, intent.Amount, intent.Currency, description, ct);

        if (result.Success)
        {
            intent.ProviderChargeId = result.ChargeId;
            await db.SaveChangesAsync(ct);
        }

        return (intent, result);
    }

    /// <summary>
    /// Creates a gift subscription checkout intent (issue #102).
    /// </summary>
    public async Task<(SubscriptionGift Gift, PaymentIntent Intent, PayOSPaymentResult Result)> CreateGiftCheckoutAsync(
        User sender,
        string recipientEmail,
        string plan,
        bool isYearly,
        string? message,
        string? promoCode,
        string? currency,
        CancellationToken ct)
    {
        var normalizedRecipient = recipientEmail.Trim().ToLowerInvariant();
        var intent = await CreateIntentAsync(sender, plan, isYearly, PaymentProviders.PayOS, promoCode, currency, ct, isGift: true);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var gift = new SubscriptionGift
        {
            Id = Guid.CreateVersion7(),
            SenderUserId = sender.Id,
            RecipientEmail = normalizedRecipient,
            Plan = intent.Plan,
            Billing = intent.Billing,
            Months = isYearly ? 12 : 1,
            Message = string.IsNullOrWhiteSpace(message)
                ? null
                : message.Trim()[..Math.Min(message.Trim().Length, SubscriptionGift.MessageMaxLength)],
            Token = token,
            Status = SubscriptionGift.StatusPendingPayment,
            PaymentIntentId = intent.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(SubscriptionGift.ClaimWindowDays),
        };

        db.SubscriptionGifts.Add(gift);
        await db.SaveChangesAsync(ct);

        var description = $"{CodePrefix}{intent.Code[^CodeRandomLength..]}";
        var result = await payOS.CreatePaymentLinkAsync(intent.PayOSOrderCode, (int)intent.Amount, description, ct);

        return (gift, intent, result);
    }

    /// <summary>Whether the crypto rail is offered at all — no key configured, no button.</summary>
    public bool CryptoAvailable => crypto.IsConfigured;

    private async Task<PaymentIntent> CreateIntentAsync(
        User user, string plan, bool isYearly, string provider, string? promoCode, string? requestedCurrency, CancellationToken ct, bool isGift = false)
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

        var credit = 0;
        if (!isGift)
        {
            // Proration (issue #114): only for self-upgrades, never for gifts.
            var current = await db.Subscriptions
                .AsNoTracking()
                .Where(s => s.UserId == user.Id)
                .OrderByDescending(s => s.CurrentPeriodEnd)
                .FirstOrDefaultAsync(ct);

            if (current is not null && Subscriptions.SubscriptionLifecycle.IsUpgrade(current.Plan, plan))
            {
                var paidKey = PlanCatalog.PricingKey(current.Plan, current.BillingPeriod == BillingPeriod.Yearly);
                var paidAmount = currency == PricingKeys.DefaultCurrency
                    ? config.GetValue(paidKey, PlanCatalog.DefaultAmount(current.Plan, current.BillingPeriod == BillingPeriod.Yearly, currency))
                    : PlanCatalog.DefaultAmount(current.Plan, current.BillingPeriod == BillingPeriod.Yearly, currency);

                credit = Subscriptions.SubscriptionLifecycle.ProrationCredit(
                    current, paidAmount, listAmount, DateTime.UtcNow);
            }
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

    public async Task<bool> FinalizeByChargeIdAsync(string chargeId, CancellationToken ct)
    {
        var intent = await db.PaymentIntents
            .Where(i => i.ProviderChargeId == chargeId)
            .Where(i => i.Status == PaymentIntentStatus.Pending)
            .FirstOrDefaultAsync(ct);

        if (intent is null)
        {
            return await db.PaymentIntents
                .AsNoTracking()
                .AnyAsync(i => i.ProviderChargeId == chargeId && i.Status == PaymentIntentStatus.Completed, ct);
        }

        return await CompleteAsync(intent, chargeId, ct);
    }

    /// <summary>Everything that happens once money has moved, whichever rail moved it.</summary>
    public async Task<bool> CompleteAsync(PaymentIntent intent, string invoiceId, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == intent.UserId, ct);
        if (user is null)
        {
            return false;
        }

        // Check whether this payment intent is for a one-time transfer pass (issue #85) or gift subscription (issue #102).
        var transferPass = await db.TransferCreditPasses.FirstOrDefaultAsync(p => p.PaymentIntentId == intent.Id, ct);
        var gift = transferPass is null
            ? await db.SubscriptionGifts.FirstOrDefaultAsync(g => g.PaymentIntentId == intent.Id, ct)
            : null;

        if (transferPass is not null)
        {
            transferPass.Status = TransferCreditPass.StatusAvailable;
        }
        else if (gift is not null)
        {
            gift.Status = SubscriptionGift.StatusClaimable;
            if (emailSender.IsConfigured)
            {
                var baseUrl = (config["Email:BaseUrl"] ?? "https://quicksharecloud.com").TrimEnd('/');
                var claimUrl = $"{baseUrl}/gift/claim?token={Uri.EscapeDataString(gift.Token)}";
                var html = $"<p><strong>{System.Net.WebUtility.HtmlEncode(user.Name)}</strong> gifted you QuickShareCloud {PlanCatalog.Get(gift.Plan).Display} ({gift.Months} month(s)).</p>"
                    + (string.IsNullOrEmpty(gift.Message) ? string.Empty : $"<blockquote>{System.Net.WebUtility.HtmlEncode(gift.Message)}</blockquote>")
                    + $"<p><a href=\"{claimUrl}\">Claim your gift subscription</a></p>";
                await emailSender.SendAsync(gift.RecipientEmail, gift.RecipientEmail, $"{user.Name} gifted you QuickShareCloud {PlanCatalog.Get(gift.Plan).Display}", html, ct);
            }
        }
        else
        {
            await SubscriptionService.HandlePaymentAsync(user, intent.Plan, intent.Billing, db, ct);
            await QuickShare.Features.Affiliates.AffiliateService.RecordPaidConversionAsync(db, user, intent, ct);
        }

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
