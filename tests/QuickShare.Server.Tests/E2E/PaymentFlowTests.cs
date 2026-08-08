using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments.Subscriptions;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// PayOS upgrade flow: upgrade POST creates a PaymentIntent + checkout link (PayOS stubbed),
/// webhook rejects bad signatures, and subscription activation upgrades the plan.
/// </summary>
public sealed class PaymentFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PaymentFlowTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UpgradePost_CreatesPaymentIntent_AndShowsStubCheckout()
    {
        var client = _factory.CreateAuthenticatedClient();

        var getResponse = await client.GetAsync("/upgrade?plan=pro");
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        var token = AntiforgeryHelper.ExtractToken(getHtml);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Plan", "pro"),
            new KeyValuePair<string, string>("Billing", "monthly"),
        });
        var postResponse = await client.PostAsync("/upgrade?plan=pro", formContent);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intent = await db.PaymentIntents.AsNoTracking()
            .Where(i => i.UserId == _factory.TestUserId && i.Status == PaymentIntentStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .FirstOrDefaultAsync();
        Assert.NotNull(intent);
        Assert.Equal("pro", intent.Plan);
        Assert.Equal("monthly", intent.Billing);
        Assert.True(intent.Amount > 0);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/payments/payos/webhook", new
        {
            code = "00",
            description = "success",
            success = true,
            data = new
            {
                orderCode = 123456789L,
                amount = 50000L,
                description = "QSC-test",
                currency = "VND",
            },
            signature = "definitely-not-a-valid-signature",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SubscriptionActivation_UpgradesUserPlan()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = "Payer",
            Email = $"payer-{Guid.NewGuid():N}@quickshare.test",
            AvatarUrl = "",
            Plan = PlanTierLimits.Free,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var subscription = await SubscriptionService.HandlePaymentAsync(
            user, PlanTierLimits.Pro, "monthly", db, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(PlanTierLimits.Pro, user.Plan);
        Assert.Equal(user.Id, subscription.UserId);
        Assert.True(subscription.CurrentPeriodEnd > DateTime.UtcNow.AddDays(27),
            "Monthly subscription should extend roughly a month out");
    }
}
