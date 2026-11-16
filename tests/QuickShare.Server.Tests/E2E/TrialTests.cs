using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data.Entities;
using QuickShare.Features.Payments.Subscriptions;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// 7-day Pro trial (issue #65): one per account, no card, auto-downgrade on
/// expiry, payment clears the trial marker.
/// </summary>
public sealed class TrialTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public TrialTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task StartTrial_FreeUser_GetsProWithTrialEnd()
    {
        var user = await _factory.SeedUserAsync(Guid.NewGuid().ToString(), QuickShare.Infrastructure.Constants.PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        var response = await client.PostAsync("/api/subscriptions/trial", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StartTrialResponse>();
        Assert.NotNull(body);
        Assert.Equal(QuickShare.Infrastructure.Constants.PlanTierLimits.Pro, body!.Plan);

        var expected = DateTime.UtcNow.AddDays(7);
        Assert.True(Math.Abs((body.TrialEndsAt - expected).TotalMinutes) < 5);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var dbUser = await db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(QuickShare.Infrastructure.Constants.PlanTierLimits.Pro, dbUser.Plan);
    }

    [Fact]
    public async Task StartTrial_Twice_Is409()
    {
        var user = await _factory.SeedUserAsync(Guid.NewGuid().ToString(), QuickShare.Infrastructure.Constants.PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        var first = await client.PostAsync("/api/subscriptions/trial", null);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostAsync("/api/subscriptions/trial", null);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task StartTrial_ProUser_Is409()
    {
        var user = await _factory.SeedUserAsync(Guid.NewGuid().ToString(), QuickShare.Infrastructure.Constants.PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(user.Id.ToString());

        var response = await client.PostAsync("/api/subscriptions/trial", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task TrialExpiry_DowngradesToFree()
    {
        var user = await _factory.SeedUserAsync(Guid.NewGuid().ToString(), QuickShare.Infrastructure.Constants.PlanTierLimits.Pro);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var past = DateTime.UtcNow.AddDays(-1);
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Plan = QuickShare.Infrastructure.Constants.PlanTierLimits.Pro,
            Status = SubscriptionStatus.Active,
            BillingPeriod = BillingPeriod.Monthly,
            CurrentPeriodStart = past.AddDays(-7),
            CurrentPeriodEnd = past,
            TrialEndsAt = past
        });
        await db.SaveChangesAsync();

        var expired = await SubscriptionService.ExpireStaleAsync(db, CancellationToken.None);
        Assert.Equal(1, expired);

        var dbUser = await db.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal(QuickShare.Infrastructure.Constants.PlanTierLimits.Free, dbUser.Plan);
    }

    [Fact]
    public async Task PaymentDuringTrial_ClearsTrialMarker()
    {
        var user = await _factory.SeedUserAsync(Guid.NewGuid().ToString(), QuickShare.Infrastructure.Constants.PlanTierLimits.Pro);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var now = DateTime.UtcNow;
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Plan = QuickShare.Infrastructure.Constants.PlanTierLimits.Pro,
            Status = SubscriptionStatus.Active,
            BillingPeriod = BillingPeriod.Monthly,
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddDays(7),
            TrialEndsAt = now.AddDays(7)
        });
        await db.SaveChangesAsync();

        await SubscriptionService.HandlePaymentAsync(user, QuickShare.Infrastructure.Constants.PlanTierLimits.Pro, "Monthly", db, CancellationToken.None);

        var sub = await db.Subscriptions.FirstAsync(s => s.UserId == user.Id);
        Assert.Null(sub.TrialEndsAt);
        Assert.True(sub.CurrentPeriodEnd > now.AddDays(29));
    }
}
