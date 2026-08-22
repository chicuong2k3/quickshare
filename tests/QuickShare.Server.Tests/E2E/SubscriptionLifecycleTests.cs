using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments.Promos;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The subscription lifecycle: promo codes (#98), proration (#114), pause (#229) and the cancel
/// flow (#174).
///
/// This is money, so the properties are stated as things that must never happen:
///
/// - A promo code must never be redeemable twice by one account, and quoting must never consume
///   one — otherwise a hundred-use code runs out on twenty abandoned checkouts.
/// - A discount must never make a price negative.
/// - A pause must never grant free time and must never burn paid time. Both failure modes are
///   why people distrust a pause button.
/// - Proration must never pay out. An upgrade can be free; it cannot be a refund.
/// </summary>
public sealed class SubscriptionLifecycleTests : IClassFixture<TestWebAppFactory>
{
    private const int MonthlyAmount = 50_000;

    private readonly TestWebAppFactory _factory;

    public SubscriptionLifecycleTests(TestWebAppFactory factory) => _factory = factory;

    // ── #98 promo codes ──

    [Fact]
    public async Task Promo_QuotesADiscountWithoutConsumingIt()
    {
        var (client, userId) = await AccountAsync();
        var code = await SeedCodeAsync(PromoKind.Percent, 25);

        for (var i = 0; i < 3; i++)
        {
            var quote = await QuoteAsync(client, code, MonthlyAmount);
            Assert.True(quote.Valid);
            Assert.Equal(12_500, quote.DiscountAmount);
            Assert.Equal(37_500, quote.FinalAmount);
        }

        // Three quotes, no redemptions. An abandoned checkout must not burn a limited code.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, (await db.PromoCodes.AsNoTracking().FirstAsync(p => p.Code == code)).RedeemedCount);
        Assert.False(await db.PromoRedemptions.AsNoTracking().AnyAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task Promo_CannotBeRedeemedTwiceByTheSameAccount()
    {
        var (_, userId) = await AccountAsync();
        var code = await SeedCodeAsync(PromoKind.Percent, 50);

        Assert.True(await RedeemAsync(code, userId));
        // The unique index is the enforcement, not the check before it.
        Assert.False(await RedeemAsync(code, userId));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.PromoRedemptions.AsNoTracking().CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task Promo_StopsAtItsRedemptionLimit()
    {
        var code = await SeedCodeAsync(PromoKind.Fixed, 10_000, maxRedemptions: 1);

        var (_, firstUser) = await AccountAsync();
        var (secondClient, secondUser) = await AccountAsync();

        Assert.True(await RedeemAsync(code, firstUser));

        var quote = await QuoteAsync(secondClient, code, MonthlyAmount);
        Assert.False(quote.Valid);
        Assert.False(await RedeemAsync(code, secondUser));
    }

    [Fact]
    public async Task Promo_IsRefusedWhenExpiredOrRevoked()
    {
        var (client, _) = await AccountAsync();

        var expired = await SeedCodeAsync(PromoKind.Percent, 10, expiresAt: DateTime.UtcNow.AddMinutes(-1));
        Assert.False((await QuoteAsync(client, expired, MonthlyAmount)).Valid);

        var revoked = await SeedCodeAsync(PromoKind.Percent, 10, revoked: true);
        Assert.False((await QuoteAsync(client, revoked, MonthlyAmount)).Valid);
    }

    [Fact]
    public async Task Promo_IsCaseInsensitive()
    {
        var (client, _) = await AccountAsync();
        var code = await SeedCodeAsync(PromoKind.Percent, 20);

        // The one certainty about a code on a conference sticker is that somebody types it
        // in lower case.
        var quote = await QuoteAsync(client, code.ToLowerInvariant(), MonthlyAmount);

        Assert.True(quote.Valid);
    }

    [Fact]
    public async Task Promo_IsRefusedForAPlanItDoesNotCover()
    {
        var (client, _) = await AccountAsync();
        var code = await SeedCodeAsync(PromoKind.Percent, 30, appliesToPlans: PlanTierLimits.Studio);

        var quote = await QuoteAsync(client, code, MonthlyAmount, plan: PlanTierLimits.Pro);

        Assert.False(quote.Valid);
        Assert.Equal(ErrorCodes.PromoCodeNotApplicable, quote.Error);
    }

    [Theory]
    [InlineData(PromoKind.Percent, 100, 50_000, 0)]
    [InlineData(PromoKind.Percent, 10, 50_000, 45_000)]
    [InlineData(PromoKind.Fixed, 999_999, 50_000, 0)]
    [InlineData(PromoKind.Fixed, 20_000, 50_000, 30_000)]
    public void Promo_NeverProducesANegativePrice(string kind, int value, int amount, int expected)
    {
        // A fixed-amount code larger than the price must make the purchase free, never negative.
        var promo = new PromoCode { Kind = kind, Value = value };

        Assert.Equal(expected, promo.ApplyTo(amount));
    }

    // ── #114 proration ──

    [Fact]
    public void Proration_CreditsTheUnusedPortionOfAnUpgrade()
    {
        var now = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new Subscription
        {
            Status = SubscriptionStatus.Active,
            Plan = PlanTierLimits.Pro,
            CurrentPeriodStart = now.AddDays(-15),
            CurrentPeriodEnd = now.AddDays(15),
        };

        // Half the month left on a 50,000 plan is 25,000 already paid for.
        var credit = SubscriptionLifecycle.ProrationCredit(subscription, 50_000, 200_000, now);

        Assert.InRange(credit, 24_000, 25_100);
    }

    [Fact]
    public void Proration_NeverExceedsTheNewPrice()
    {
        var now = DateTime.UtcNow;
        var subscription = new Subscription
        {
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now.AddDays(-1),
            CurrentPeriodEnd = now.AddDays(364),
        };

        // A nearly-full year of Studio credited against one month of Pro: an upgrade can be
        // free, but it can never be a payout.
        var credit = SubscriptionLifecycle.ProrationCredit(subscription, 2_000_000, 50_000, now);

        Assert.Equal(50_000, credit);
    }

    [Fact]
    public void Proration_DoesNotApplyToATrialOrAPause()
    {
        var now = DateTime.UtcNow;

        var trial = new Subscription
        {
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now.AddDays(-1),
            CurrentPeriodEnd = now.AddDays(6),
            TrialEndsAt = now.AddDays(6),
        };
        // Nothing was paid, so there is nothing to credit.
        Assert.Equal(0, SubscriptionLifecycle.ProrationCredit(trial, 50_000, 200_000, now));

        var paused = new Subscription
        {
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = now.AddDays(-1),
            CurrentPeriodEnd = now.AddDays(20),
            PausedAt = now.AddDays(-1),
        };
        // Paused time is a stopped clock; crediting it would pay for days not being consumed.
        Assert.Equal(0, SubscriptionLifecycle.ProrationCredit(paused, 50_000, 200_000, now));
    }

    [Theory]
    [InlineData(PlanTierLimits.Free, PlanTierLimits.Pro, true)]
    [InlineData(PlanTierLimits.Pro, PlanTierLimits.Studio, true)]
    [InlineData(PlanTierLimits.Studio, PlanTierLimits.Pro, false)]
    [InlineData(PlanTierLimits.Pro, PlanTierLimits.Pro, false)]
    public void Proration_OnlyAppliesUpwards(string from, string to, bool expected)
    {
        // A downgrade keeps the better plan until the period ends; refunding the difference
        // would mean paying money back for a service already delivered.
        Assert.Equal(expected, SubscriptionLifecycle.IsUpgrade(from, to));
    }

    // ── #229 pause ──

    [Fact]
    public async Task Pause_BanksTheRemainingDaysAndDropsToFree()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 20);

        var response = await client.PostAsync("/api/billing/subscription/pause", null);
        response.EnsureSuccessStatusCode();
        var state = (await response.Content.ReadFromJsonAsync<SubscriptionStateDto>())!;

        Assert.True(state.IsPaused);
        Assert.InRange(state.PausedDaysBanked, 20, 21);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Nothing is being charged for, so nothing paid-for is being provided.
        Assert.Equal(PlanTierLimits.Free, (await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId)).Plan);
    }

    [Fact]
    public async Task Resume_HandsBackExactlyTheBankedDays()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 20);

        await client.PostAsync("/api/billing/subscription/pause", null);

        // Time passes while paused. It must cost nothing and earn nothing.
        await ShiftPauseBackAsync(userId, TimeSpan.FromDays(30));

        var response = await client.PostAsync("/api/billing/subscription/resume", null);
        response.EnsureSuccessStatusCode();
        var state = (await response.Content.ReadFromJsonAsync<SubscriptionStateDto>())!;

        Assert.False(state.IsPaused);
        // ~20 days from now, not 20 days from a month ago and not 50.
        Assert.InRange((state.CurrentPeriodEnd - DateTime.UtcNow).TotalDays, 19, 21.5);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PlanTierLimits.Pro, (await db.Users.AsNoTracking().FirstAsync(u => u.Id == userId)).Plan);
    }

    [Fact]
    public async Task Pause_IsRefusedTwice()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 10);

        (await client.PostAsync("/api/billing/subscription/pause", null)).EnsureSuccessStatusCode();
        var second = await client.PostAsync("/api/billing/subscription/pause", null);

        // Otherwise each press re-banks the days and the hold never ends.
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── #174 cancel flow ──

    [Fact]
    public async Task Cancel_RecordsTheReasonAndKeepsThePlanToPeriodEnd()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 12);

        var response = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new
        {
            reason = CancellationReasons.TooExpensive,
            feedback = "Great tool, wrong budget.",
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().FirstAsync(s => s.UserId == userId);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.Equal(CancellationReasons.TooExpensive, subscription.CancellationReason);
        Assert.Equal("Great tool, wrong budget.", subscription.CancellationFeedback);
        // Cancelling is not a refund request: the paid period still runs out.
        Assert.True(subscription.CurrentPeriodEnd > DateTime.UtcNow);
    }

    [Fact]
    public async Task Cancel_TakingTheOfferPausesInsteadOfEnding()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 25);

        var response = await client.PostAsJsonAsync("/api/billing/subscription/cancel", new
        {
            reason = CancellationReasons.TemporaryBreak,
            acceptPause = true,
        });
        response.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().FirstAsync(s => s.UserId == userId);

        Assert.True(subscription.IsPaused);
        Assert.NotEqual(SubscriptionStatus.Cancelled, subscription.Status);
        // Recorded anyway: a save that is not counted looks like a customer who never tried to
        // leave, and then nobody knows the offer is working.
        Assert.Equal(CancellationReasons.TemporaryBreak, subscription.CancellationReason);
    }

    [Fact]
    public async Task SaveOffer_OffersAPauseToSomebodyTakingABreak()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 20);

        var offer = await client.GetFromJsonAsync<SaveOfferDto>(
            $"/api/billing/subscription/save-offer?reason={CancellationReasons.TemporaryBreak}");

        // Somebody taking a break does not want money off. Offering a discount is answering a
        // question they did not ask.
        Assert.True(offer!.OfferPause);
        Assert.Null(offer.PromoCode);
    }

    [Fact]
    public async Task SaveOffer_IsMadeOnlyOnce()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 20);

        await client.PostAsJsonAsync("/api/billing/subscription/cancel",
            new { reason = CancellationReasons.NotUsingIt, acceptPause = true });
        await client.PostAsync("/api/billing/subscription/resume", null);

        var offer = await client.GetFromJsonAsync<SaveOfferDto>(
            $"/api/billing/subscription/save-offer?reason={CancellationReasons.NotUsingIt}");

        // An offer that reappears every time somebody tries to leave is an obstacle, not a
        // retention tool, and they will remember which it was.
        Assert.True(offer!.AlreadyOffered);
        Assert.False(offer.OfferPause);
    }

    [Theory]
    [InlineData("too_expensive", false)]
    [InlineData("temporary_break", true)]
    [InlineData("not_using_it", true)]
    [InlineData("nonsense-value", false)]
    public void SaveOffer_MatchesTheOfferToTheReason(string reason, bool expectPause)
    {
        Assert.Equal(expectPause, CancellationReasons.PreferPause(CancellationReasons.Normalize(reason)));
    }

    // ── The screens ──

    [Fact]
    public async Task BillingPage_OffersAHoldAndAsksWhyOnTheWayOut()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 20);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/billing");
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // The hold sits beside the exit, not behind it: somebody taking a break should not have
        // to cancel in order to stop paying.
        Assert.Contains("value=\"pause\"", html, StringComparison.Ordinal);
        Assert.Contains("cancel-reason", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BillingPage_ShowsTheBankedDaysWhilePaused()
    {
        var (client, userId) = await AccountAsync();
        await SeedSubscriptionAsync(userId, PlanTierLimits.Pro, daysLeft: 18);
        (await client.PostAsync("/api/billing/subscription/pause", null)).EnsureSuccessStatusCode();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/account/billing");
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        Assert.Contains("value=\"resume\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpgradePage_OffersAPromoField()
    {
        var (client, _) = await AccountAsync();

        var html = await client.GetStringAsync("/upgrade?plan=pro");

        // Without this the promo endpoints are an API nobody can reach — the defect the audit
        // spent a batch correcting.
        Assert.Contains("upgrade-promo", html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private async Task<string> SeedCodeAsync(
        string kind, int value, int? maxRedemptions = null, DateTime? expiresAt = null,
        bool revoked = false, string? appliesToPlans = null)
    {
        var code = $"TEST{Guid.NewGuid():N}"[..12].ToUpperInvariant();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.PromoCodes.Add(new PromoCode
        {
            Id = Guid.CreateVersion7(),
            Code = code,
            Kind = kind,
            Value = value,
            MaxRedemptions = maxRedemptions,
            ExpiresAt = expiresAt,
            RevokedAt = revoked ? DateTime.UtcNow : null,
            AppliesToPlans = appliesToPlans,
        });
        await db.SaveChangesAsync();

        return code;
    }

    private static async Task<PromoQuoteDto> QuoteAsync(
        HttpClient client, string code, int amount, string plan = PlanTierLimits.Pro)
    {
        var response = await client.PostAsJsonAsync("/api/billing/promo/quote",
            new { code, plan, amount });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PromoQuoteDto>())!;
    }

    private async Task<bool> RedeemAsync(string code, Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var promos = scope.ServiceProvider.GetRequiredService<PromoCodeService>();
        return await promos.RedeemAsync(code, userId, PlanTierLimits.Pro, MonthlyAmount, CancellationToken.None);
    }

    private async Task SeedSubscriptionAsync(Guid userId, string plan, int daysLeft)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Plan = plan,
            Status = SubscriptionStatus.Active,
            BillingPeriod = BillingPeriod.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.AddDays(daysLeft - 30),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(daysLeft),
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Pretends the hold started earlier, so elapsed time does not have to be waited out.</summary>
    private async Task ShiftPauseBackAsync(Guid userId, TimeSpan by)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var subscription = await db.Subscriptions.FirstAsync(s => s.UserId == userId);
        subscription.PausedAt = DateTime.UtcNow - by;
        await db.SaveChangesAsync();
    }
}
