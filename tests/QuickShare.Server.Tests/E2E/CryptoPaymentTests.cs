using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments;
using QuickShare.Features.Payments.Crypto;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Crypto payments (#225).
///
/// The rail is different; the money path is not. What is worth asserting is exactly that: the
/// same intent, the same price, the same plan activation — plus the one thing that is genuinely
/// new, which is that a webhook nobody can forge is the only thing that moves a plan, because
/// the buyer's browser is long gone before a chain confirms.
/// </summary>
public sealed class CryptoPaymentTests : IClassFixture<TestWebAppFactory>
{
    private const string WebhookUrl = "/api/payments/crypto/webhook";
    private const string Confirmed = "charge:confirmed";

    private readonly TestWebAppFactory _factory;

    public CryptoPaymentTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Checkout_CreatesAPendingIntentOnTheCryptoRail()
    {
        var user = await UserAsync();

        var intent = await CheckoutAsync(user, PlanTierLimits.Pro);

        Assert.Equal(PaymentProviders.Crypto, intent.Provider);
        Assert.Equal(PaymentIntentStatus.Pending, intent.Status);
        // The provider's own id is what the webhook will arrive knowing.
        Assert.False(string.IsNullOrEmpty(intent.ProviderChargeId));
    }

    [Fact]
    public async Task Checkout_ChargesTheSamePriceTheCardRailWould()
    {
        var user = await UserAsync();

        var card = await CardIntentAsync(user, PlanTierLimits.Pro);
        var crypto = await CheckoutAsync(await UserAsync(), PlanTierLimits.Pro);

        // A second provider that computed its own prices would be a second product.
        Assert.Equal(card.Amount, crypto.Amount);
        Assert.Equal(card.Currency, crypto.Currency);
    }

    [Fact]
    public async Task ConfirmedWebhook_ActivatesThePlan()
    {
        var user = await UserAsync();
        var intent = await CheckoutAsync(user, PlanTierLimits.Pro);

        var response = await PostWebhookAsync(Confirmed, intent.ProviderChargeId!);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var completed = await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.Id == intent.Id);
        Assert.Equal(PaymentIntentStatus.Completed, completed.Status);

        var upgraded = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.Equal(PlanTierLimits.Pro, upgraded.Plan);
    }

    [Fact]
    public async Task ForgedWebhook_ChangesNothing()
    {
        var user = await UserAsync();
        var intent = await CheckoutAsync(user, PlanTierLimits.Pro);

        var response = await PostWebhookAsync(Confirmed, intent.ProviderChargeId!, signature: "deadbeef");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // The signature is the whole security of this feature: no redirect proves payment here.
        Assert.Equal(PlanTierLimits.Free, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Plan);
        Assert.Equal(PaymentIntentStatus.Pending,
            (await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.Id == intent.Id)).Status);
    }

    [Fact]
    public async Task UnconfirmedEvents_AreAcknowledgedAndIgnored()
    {
        var user = await UserAsync();
        var intent = await CheckoutAsync(user, PlanTierLimits.Pro);

        var response = await PostWebhookAsync("charge:pending", intent.ProviderChargeId!);

        // 200 so the provider stops retrying something that is not a failure; the plan waits.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PlanTierLimits.Free, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Plan);
    }

    [Fact]
    public async Task RepeatedWebhook_DoesNotChargeTheAccountTwice()
    {
        var user = await UserAsync();
        var intent = await CheckoutAsync(user, PlanTierLimits.Pro);

        // Providers retry until they get a 200, so the same event arrives more than once.
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(Confirmed, intent.ProviderChargeId!)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync(Confirmed, intent.ProviderChargeId!)).StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var payments = await db.PaymentEvents.AsNoTracking()
            .Where(e => e.UserId == user.Id && e.EventType == PaymentEventTypes.Payment)
            .ToListAsync();

        Assert.Single(payments);
    }

    [Fact]
    public async Task WebhookForAnUnknownCharge_IsAcknowledgedWithoutActivatingAnything()
    {
        var user = await UserAsync();
        await CheckoutAsync(user, PlanTierLimits.Pro);

        var response = await PostWebhookAsync(Confirmed, "no-such-charge");

        // Not something the provider can fix by sending it again — an error earns hours of retries.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PlanTierLimits.Free, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Plan);
    }

    [Fact]
    public void Signature_IsCheckedOverTheRawBodyInConstantTime()
    {
        using var scope = _factory.Services.CreateScope();
        var crypto = scope.ServiceProvider.GetRequiredService<CryptoCheckoutService>();

        const string Body = """{"event":{"type":"charge:confirmed","data":{"code":"ABC"}}}""";

        Assert.True(crypto.VerifySignature(Body, Sign(Body)));
        // One byte different in the body, and the same signature must stop meaning anything.
        Assert.False(crypto.VerifySignature(Body.Replace("ABC", "ABD", StringComparison.Ordinal), Sign(Body)));
        Assert.False(crypto.VerifySignature(Body, null));
    }

    // ── Helpers ──

    private async Task<User> UserAsync() =>
        await _factory.SeedUserAsync(Guid.NewGuid().ToString());

    private async Task<PaymentIntent> CheckoutAsync(User user, string plan)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

        var (intent, result) = await checkout.CreateCryptoCheckoutAsync(user, plan, isYearly: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.False(string.IsNullOrEmpty(result.HostedUrl));

        return intent;
    }

    private async Task<PaymentIntent> CardIntentAsync(User user, string plan)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var checkout = scope.ServiceProvider.GetRequiredService<CheckoutService>();

        var (intent, _) = await checkout.CreatePaymentLinkAsync(user, plan, isYearly: false, CancellationToken.None);

        return intent;
    }

    private Task<HttpResponseMessage> PostWebhookAsync(string type, string chargeId, string? signature = null)
    {
        // Concatenated rather than interpolated: the closing braces of this JSON collide
        // with raw-string interpolation, and the exact bytes are what gets signed.
        var body = "{\"event\":{\"type\":\"" + type + "\",\"data\":{\"code\":\"" + chargeId + "\"}}}";

        var request = new HttpRequestMessage(HttpMethod.Post, WebhookUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.TryAddWithoutValidation(CryptoCheckoutService.SignatureHeader, signature ?? Sign(body));

        return _factory.CreateClient().SendAsync(request);
    }

    private static string Sign(string body) =>
        Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(TestWebAppFactory.CryptoWebhookSecret),
            Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
}
