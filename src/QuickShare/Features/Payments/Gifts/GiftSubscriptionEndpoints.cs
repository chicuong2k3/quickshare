using System.Text.Json;
using QuickShare.Features.Payments;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Payments.Gifts;

public sealed class CreateGiftSubscriptionRequest
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string Plan { get; set; } = PlanTierLimits.Pro;
    public string Billing { get; set; } = BillingPeriods.Monthly;
    public string? Message { get; set; }
    public string? PromoCode { get; set; }
    public string? Currency { get; set; }
}

public sealed record GiftSubscriptionDto(
    Guid Id,
    Guid SenderUserId,
    string SenderName,
    string RecipientEmail,
    string Plan,
    string Billing,
    int Months,
    string? Message,
    string Token,
    string Status,
    Guid? PaymentIntentId,
    DateTime? ClaimedAt,
    DateTime ExpiresAt);

/// <summary>
/// POST /api/gifts — create a gift subscription checkout intent (issue #102).
/// Self-gifting is refused so promo/referral loops cannot be gamed.
/// </summary>
public sealed class CreateGiftSubscriptionEndpoint(
    AppDbContext db,
    CheckoutService checkout) : Endpoint<CreateGiftSubscriptionRequest, GiftSubscriptionDto>
{
    public override void Configure()
    {
        Post(RoutePaths.GiftsApi);
        Description(b => b.WithTags(ApiTags.Payments));
        Summary(s =>
        {
            s.Summary = "Buy a Pro or Studio subscription as a gift for another email";
            s.Description = "Issue #102. Refuses self-gifting.";
        });
    }

    public override async Task HandleAsync(CreateGiftSubscriptionRequest req, CancellationToken ct)
    {
        var sender = await HttpContext.User.GetUserAsync(db, ct);
        var recipientEmail = req.RecipientEmail?.Trim().ToLowerInvariant() ?? string.Empty;

        if (string.IsNullOrEmpty(recipientEmail)
            || !recipientEmail.Contains('@', StringComparison.Ordinal)
            || recipientEmail.Length > SubscriptionGift.EmailMaxLength)
        {
            ThrowError(ErrorCodes.GiftEmailInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (string.Equals(recipientEmail, sender.Email, StringComparison.OrdinalIgnoreCase))
        {
            ThrowError(ErrorCodes.GiftSelfNotAllowed, StatusCodes.Status400BadRequest);
            return;
        }

        var isYearly = string.Equals(req.Billing, BillingPeriods.Yearly, StringComparison.OrdinalIgnoreCase);
        var (gift, _, _) = await checkout.CreateGiftCheckoutAsync(
            sender, recipientEmail, req.Plan, isYearly, req.Message, req.PromoCode, req.Currency, ct);

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = sender.Id,
            EventType = SecurityEventType.SubscriptionGiftCreated,
            IpHash = string.Empty,
            UserAgentLabel = nameof(CreateGiftSubscriptionEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { gift.Id, gift.Plan, gift.Billing }),
        });
        await db.SaveChangesAsync(ct);

        await Send.OkAsync(Map(gift, sender.Name), ct);
    }

    internal static GiftSubscriptionDto Map(SubscriptionGift g, string senderName) => new(
        g.Id,
        g.SenderUserId,
        senderName,
        g.RecipientEmail,
        g.Plan,
        g.Billing,
        g.Months,
        g.Message,
        g.Token,
        g.Status,
        g.PaymentIntentId,
        g.ClaimedAt,
        g.ExpiresAt);
}

/// <summary>GET /api/gifts — lists gifts purchased by the authenticated user (issue #102).</summary>
public sealed class ListMyGiftsEndpoint(AppDbContext db)
    : EndpointWithoutRequest<IReadOnlyList<GiftSubscriptionDto>>
{
    public override void Configure()
    {
        Get(RoutePaths.GiftsApi);
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var gifts = await db.SubscriptionGifts
            .AsNoTracking()
            .Where(g => g.SenderUserId == user.Id)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync(ct);

        await Send.OkAsync(gifts.Select(g => CreateGiftSubscriptionEndpoint.Map(g, user.Name)).ToList(), ct);
    }
}

/// <summary>GET /api/gifts/{Token} — public preview of a gift token before claiming (issue #102).</summary>
public sealed class GetGiftByTokenEndpoint(AppDbContext db) : EndpointWithoutRequest<GiftSubscriptionDto>
{
    public override void Configure()
    {
        Get(RoutePaths.GiftsApi + "/{Token}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var token = (Route<string>("Token") ?? string.Empty).Trim().ToLowerInvariant();
        var gift = await db.SubscriptionGifts
            .AsNoTracking()
            .Include(g => g.Sender)
            .FirstOrDefaultAsync(g => g.Token == token, ct);

        if (gift is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(CreateGiftSubscriptionEndpoint.Map(gift, gift.Sender?.Name ?? "A friend"), ct);
    }
}

/// <summary>
/// POST /api/gifts/{Token}/claim — claims a paid gift subscription for the authenticated user (issue #102).
/// Activates or extends the recipient's subscription without charging them again.
/// </summary>
public sealed class ClaimGiftSubscriptionEndpoint(AppDbContext db)
    : EndpointWithoutRequest<GiftSubscriptionDto>
{
    public override void Configure()
    {
        Post(RoutePaths.GiftsApi + "/{Token}/claim");
        Description(b => b.WithTags(ApiTags.Payments));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var recipient = await HttpContext.User.GetUserAsync(db, ct);
        var token = (Route<string>("Token") ?? string.Empty).Trim().ToLowerInvariant();

        var gift = await db.SubscriptionGifts
            .Include(g => g.Sender)
            .FirstOrDefaultAsync(g => g.Token == token, ct);

        if (gift is null)
        {
            ThrowError(ErrorCodes.GiftNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (gift.SenderUserId == recipient.Id)
        {
            ThrowError(ErrorCodes.GiftSelfNotAllowed, StatusCodes.Status400BadRequest);
            return;
        }

        if (gift.Status == SubscriptionGift.StatusClaimed)
        {
            ThrowError(ErrorCodes.GiftAlreadyClaimed, StatusCodes.Status409Conflict);
            return;
        }

        if (gift.Status != SubscriptionGift.StatusClaimable || gift.ExpiresAt <= DateTime.UtcNow)
        {
            ThrowError(ErrorCodes.GiftNotClaimable, StatusCodes.Status400BadRequest);
            return;
        }

        await SubscriptionService.HandlePaymentAsync(recipient, gift.Plan, gift.Billing, db, ct);

        gift.Status = SubscriptionGift.StatusClaimed;
        gift.ClaimedByUserId = recipient.Id;
        gift.ClaimedAt = DateTime.UtcNow;

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = recipient.Id,
            EventType = SecurityEventType.SubscriptionGiftClaimed,
            IpHash = string.Empty,
            UserAgentLabel = nameof(ClaimGiftSubscriptionEndpoint),
            MetadataJson = JsonSerializer.Serialize(new { gift.Id, gift.Plan, gift.Billing, gift.SenderUserId }),
        });

        await db.SaveChangesAsync(ct);
        await UserCacheInvalidator.EvictAsync(db, recipient.Id, ct);

        await Send.OkAsync(CreateGiftSubscriptionEndpoint.Map(gift, gift.Sender?.Name ?? "A friend"), ct);
    }
}
