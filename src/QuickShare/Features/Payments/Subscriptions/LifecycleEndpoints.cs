using QuickShare.Extensions;
using QuickShare.Features.Payments.Promos;

namespace QuickShare.Features.Payments.Subscriptions;

/// <summary>What a code is worth, for the checkout screen (issue #98).</summary>
public sealed record PromoQuoteDto(bool Valid, string? Error, int DiscountAmount, int FinalAmount, string? Description);

/// <summary>Subscription state as the billing screen shows it (issues #229, #114).</summary>
public sealed record SubscriptionStateDto(
    string Plan, string Status, string BillingPeriod,
    DateTime CurrentPeriodEnd, bool IsPaused, int PausedDaysBanked, DateTime? CancelledAt);

/// <summary>
/// What the cancel flow should offer before it lets go (issue #174).
///
/// Computed server-side because the honest offer depends on the reason: somebody taking a break
/// wants a pause, not money off.
/// </summary>
public sealed record SaveOfferDto(bool OfferPause, string? PromoCode, int DiscountPercent, bool AlreadyOffered);

public sealed class PromoQuoteRequest
{
    public string? Code { get; set; }
    public string? Plan { get; set; }
    public int Amount { get; set; }
}

public sealed class CancelSubscriptionRequest
{
    public string? Reason { get; set; }
    public string? Feedback { get; set; }
    /// <summary>Set when the subscriber took the save offer instead of leaving.</summary>
    public bool AcceptPause { get; set; }
}

/// <summary>POST /api/billing/promo/quote — price a code without consuming it (issue #98).</summary>
public sealed class QuotePromoEndpoint : Endpoint<PromoQuoteRequest, PromoQuoteDto>
{
    public override void Configure()
    {
        Post("/api/billing/promo/quote");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Check a promo code and preview the discounted price";
            s.Description = "Does not consume the code — an abandoned checkout must not burn a redemption.";
        });
    }

    public override async Task HandleAsync(PromoQuoteRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        if (req.Amount <= 0)
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        var quote = await Resolve<PromoCodeService>()
            .QuoteAsync(req.Code, user.Id, req.Plan ?? PlanTierLimits.Pro, req.Amount, ct);

        await Send.OkAsync(new PromoQuoteDto(
            quote.Valid, quote.Error, quote.DiscountAmount, quote.FinalAmount, quote.Description), ct);
    }
}

/// <summary>GET /api/billing/subscription — current state, including a hold (issues #229, #174).</summary>
public sealed class GetSubscriptionStateEndpoint : EndpointWithoutRequest<SubscriptionStateDto?>
{
    public override void Configure()
    {
        Get("/api/billing/subscription");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s => s.Summary = "Current subscription state");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        await Send.OkAsync(subscription is null ? null : new SubscriptionStateDto(
            subscription.Plan, subscription.Status.ToString(), subscription.BillingPeriod.ToString(),
            subscription.CurrentPeriodEnd, subscription.IsPaused, subscription.PausedDaysBanked,
            subscription.CancelledAt), ct);
    }
}

/// <summary>
/// POST /api/billing/subscription/pause — vacation hold (issue #229).
///
/// Drops the account to Free for the duration and banks the remaining paid days. Billing is not
/// charged again until the hold is lifted, and the banked days come back in full.
/// </summary>
public sealed class PauseSubscriptionEndpoint : EndpointWithoutRequest<SubscriptionStateDto>
{
    public override void Configure()
    {
        Post("/api/billing/subscription/pause");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Pause the subscription";
            s.Description = "Remaining paid days are banked and returned on resume. The plan drops to Free meanwhile.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var subscription = await db.Subscriptions
            .Where(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null || !SubscriptionLifecycle.Pause(subscription, DateTime.UtcNow))
        {
            ThrowError(ErrorCodes.SubscriptionNotPausable, StatusCodes.Status409Conflict);
            return;
        }

        await DropToFreeAsync(db, user, ct);
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map(subscription), ct);
    }

    /// <summary>
    /// A paused subscriber is on Free. Their files stay; the paid limits do not, because the
    /// point of a hold is that nothing is being charged for.
    /// </summary>
    internal static async Task DropToFreeAsync(AppDbContext db, User user, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, PlanTierLimits.Free)
                .SetProperty(u => u.UpdatedAt, now), ct);

        user.Plan = PlanTierLimits.Free;
    }

    internal static SubscriptionStateDto Map(Subscription s) => new(
        s.Plan, s.Status.ToString(), s.BillingPeriod.ToString(),
        s.CurrentPeriodEnd, s.IsPaused, s.PausedDaysBanked, s.CancelledAt);
}

/// <summary>POST /api/billing/subscription/resume — lift a hold (issue #229).</summary>
public sealed class ResumeSubscriptionEndpoint : EndpointWithoutRequest<SubscriptionStateDto>
{
    public override void Configure()
    {
        Post("/api/billing/subscription/resume");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s => s.Summary = "Resume a paused subscription and get the banked days back");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var subscription = await db.Subscriptions
            .Where(s => s.UserId == user.Id && s.PausedAt != null)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null || !SubscriptionLifecycle.Resume(subscription, DateTime.UtcNow))
        {
            ThrowError(ErrorCodes.SubscriptionNotPaused, StatusCodes.Status409Conflict);
            return;
        }

        var now = DateTime.UtcNow;
        var plan = subscription.Plan;
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, plan)
                .SetProperty(u => u.UpdatedAt, now), ct);

        user.Plan = plan;
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(PauseSubscriptionEndpoint.Map(subscription), ct);
    }
}

/// <summary>
/// GET /api/billing/subscription/save-offer — what to offer before letting go (issue #174).
///
/// Asked for with the reason, so the offer can match it. A retention flow that answers every
/// reason with the same discount is the reason cancel flows feel like a fight.
/// </summary>
public sealed class GetSaveOfferEndpoint : EndpointWithoutRequest<SaveOfferDto>
{
    public override void Configure()
    {
        Get("/api/billing/subscription/save-offer");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s => s.Summary = "The retention offer for a stated cancellation reason");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var reason = CancellationReasons.Normalize(Query<string>("reason", isRequired: false));

        var subscription = await db.Subscriptions
            .AsNoTracking()
            .Where(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            await Send.OkAsync(new SaveOfferDto(false, null, 0, false), ct);
            return;
        }

        // Offered once. A save offer that reappears every time somebody tries to leave is not a
        // retention tool, it is an obstacle, and they will remember which it was.
        if (subscription.SaveOfferShown)
        {
            await Send.OkAsync(new SaveOfferDto(false, null, 0, true), ct);
            return;
        }

        var offerPause = CancellationReasons.PreferPause(reason);
        var promo = offerPause ? null : await FindRetentionCodeAsync(db, ct);

        await Send.OkAsync(new SaveOfferDto(
            offerPause, promo?.Code, promo?.Kind == PromoKind.Percent ? promo.Value : 0, false), ct);
    }

    /// <summary>
    /// The live retention code, if the operator has configured one.
    ///
    /// Not invented here: a discount is a commercial decision, and a hard-coded percentage in the
    /// cancel flow is a price change nobody signed off on. No code configured means no discount
    /// is offered, and the pause is still there.
    /// </summary>
    private static Task<PromoCode?> FindRetentionCodeAsync(AppDbContext db, CancellationToken ct) =>
        db.PromoCodes
            .AsNoTracking()
            .Where(p => p.Code == RetentionCode && p.RevokedAt == null)
            .FirstOrDefaultAsync(ct);

    /// <summary>The well-known code the cancel flow looks for. Created by an operator, or absent.</summary>
    public const string RetentionCode = "STAY";
}

/// <summary>
/// POST /api/billing/subscription/cancel — leave, with the reason recorded (issue #174).
///
/// Extends the existing cancel rather than replacing it: the plan still runs to the end of the
/// paid period, because cancelling is not a refund request.
/// </summary>
public sealed class CancelSubscriptionEndpoint : Endpoint<CancelSubscriptionRequest, SubscriptionStateDto>
{
    public override void Configure()
    {
        Post("/api/billing/subscription/cancel");
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Cancel the subscription, recording why";
            s.Description = "The plan stays active until the period ends. Passing acceptPause holds it instead of cancelling.";
        });
    }

    public override async Task HandleAsync(CancelSubscriptionRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var subscription = await db.Subscriptions
            .Where(s => s.UserId == user.Id && s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CurrentPeriodEnd)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            ThrowError(ErrorCodes.SubscriptionNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var reason = CancellationReasons.Normalize(req.Reason);
        subscription.SaveOfferShown = true;

        // Took the offer: hold it instead of ending it. Recorded either way — a save that is not
        // counted looks exactly like a customer who never tried to leave.
        if (req.AcceptPause && SubscriptionLifecycle.Pause(subscription, DateTime.UtcNow))
        {
            subscription.CancellationReason = reason;
            await PauseSubscriptionEndpoint.DropToFreeAsync(db, user, ct);
            await db.SaveChangesAsync(ct);

            await Send.OkAsync(PauseSubscriptionEndpoint.Map(subscription), ct);
            return;
        }

        subscription.Status = SubscriptionStatus.Cancelled;
        subscription.CancelledAt = DateTime.UtcNow;
        subscription.CancellationReason = reason;
        subscription.CancellationFeedback = string.IsNullOrWhiteSpace(req.Feedback)
            ? null
            : req.Feedback.Trim()[..Math.Min(req.Feedback.Trim().Length, Subscription.CancellationFeedbackMaxLength)];
        subscription.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(PauseSubscriptionEndpoint.Map(subscription), ct);
    }
}
