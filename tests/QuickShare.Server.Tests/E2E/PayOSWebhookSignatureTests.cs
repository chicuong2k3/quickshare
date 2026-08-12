using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PayOS;
using PayOS.Models.Webhooks;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// PayOS webhook happy path (issue #7): a webhook with a VALID signature —
/// produced by the same SDK CryptoProvider the endpoint verifies with —
/// activates the subscription and completes the intent. Complements the
/// invalid-signature 401 test in PaymentFlowTests.
/// </summary>
public sealed class PayOSWebhookSignatureTests : IClassFixture<TestWebAppFactory>
{
    // Must match the TestWebAppFactory PayOS settings.
    private const string ClientId = "test-payos-client";
    private const string ApiKey = "test-payos-api-key";
    private const string ChecksumKey = "test-payos-checksum-key";
    private const string WebhookUrl = "/api/payments/payos/webhook";

    private readonly TestWebAppFactory _factory;

    public PayOSWebhookSignatureTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<(User User, PaymentIntent Intent)> SeedPendingIntentAsync()
    {
        var user = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intent = new PaymentIntent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Code = $"QSC-{Guid.NewGuid():N}"[..12],
            PayOSOrderCode = Random.Shared.NextInt64(100_000_000, 999_999_999),
            Plan = PlanTierLimits.Pro,
            Billing = "monthly",
            Amount = 50_000,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
        };
        db.PaymentIntents.Add(intent);
        await db.SaveChangesAsync();
        return (user, intent);
    }

    /// <summary>Builds the webhook body with a signature the endpoint will accept.</summary>
    private static object SignedWebhookBody(long orderCode, long amount, string checksumKey)
    {
        // Real PayOS payloads carry fields no typed model of ours lists
        // (counterAccount*, virtualAccount*) and signs all of them — so sign the
        // exact object that goes on the wire, like PayOS does.
        var data = new
        {
            orderCode,
            amount,
            description = "QSC test payment",
            accountNumber = "0123456789",
            reference = "FT-TEST-REF",
            transactionDateTime = "2026-08-09 10:00:00",
            currency = "VND",
            paymentLinkId = "test-link-id",
            code = "00",
            desc = "success",
            counterAccountBankId = "970422",
            counterAccountBankName = "MB Bank",
            counterAccountName = "NGUYEN VAN A",
            counterAccountNumber = "9876543210",
            virtualAccountName = "QUICKSHARE",
            virtualAccountNumber = "V0123456789",
        };

        // Sign with the SDK's own CryptoProvider — the same code path the
        // endpoint verifies with, so the test cannot drift from the algorithm.
        var signature = new PayOSClient(ClientId, ApiKey, checksumKey)
            .Crypto.CreateSignatureFromObject(data, checksumKey);

        return new
        {
            code = "00",
            desc = "success",
            success = true,
            data,
            signature,
        };
    }

    [Fact]
    public async Task Webhook_ValidSignature_ActivatesPlanAndCompletesIntent()
    {
        var (user, intent) = await SeedPendingIntentAsync();
        var body = SignedWebhookBody(intent.PayOSOrderCode, (long)intent.Amount, ChecksumKey);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(WebhookUrl, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("processed", await response.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var freshUser = await db.Users.AsNoTracking().FirstAsync(u => u.Id == user.Id);
        Assert.Equal(PlanTierLimits.Pro, freshUser.Plan);
        var freshIntent = await db.PaymentIntents.AsNoTracking().FirstAsync(i => i.Id == intent.Id);
        Assert.Equal(PaymentIntentStatus.Completed, freshIntent.Status);
        Assert.NotNull(freshIntent.CompletedAt);
        Assert.NotNull(await db.PaymentEvents.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == user.Id && e.Plan == PlanTierLimits.Pro));
    }

    [Fact]
    public async Task Webhook_ValidSignature_UnknownOrderCode_IsAcknowledgedButIgnored()
    {
        var body = SignedWebhookBody(orderCode: 111_222_333_444, amount: 50_000, ChecksumKey);

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(WebhookUrl, body);
        // Signature valid but no matching intent — acknowledged (200) so PayOS stops retrying.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ignored", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Webhook_TamperedAmount_Returns401()
    {
        var (_, intent) = await SeedPendingIntentAsync();
        // Sign for one amount, send another — integrity check must reject it.
        var body = System.Text.Json.JsonSerializer.SerializeToElement(
            SignedWebhookBody(intent.PayOSOrderCode, (long)intent.Amount, ChecksumKey));
        var tampered = System.Text.Json.Nodes.JsonNode.Parse(body.GetRawText())!;
        tampered["data"]!["amount"] = 1;

        var client = _factory.CreateClient();
        var response = await client.PostAsync(WebhookUrl,
            new StringContent(tampered.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
