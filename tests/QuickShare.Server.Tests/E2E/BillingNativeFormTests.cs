using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class BillingNativeFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public BillingNativeFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task BillingGetAndPost_RenderAndPauseWithoutJavascript()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        await SeedSubscriptionAsync(user.Id);
        using var client = _factory.CreateAuthenticatedClient(key);
        var html = await client.GetStringAsync(RoutePaths.AccountBilling);
        Assert.Contains("value=\"pause\"", html);
        Assert.DoesNotContain("hx-", html);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
            ["_handler"] = "billing-pause",
            ["Action"] = "pause",
        });

        using var response = await client.PostAsync(RoutePaths.AccountBilling, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == user.Id);
        Assert.NotNull(subscription.PausedAt);
    }

    [Fact]
    public async Task BillingPost_RejectsMissingTokenWithoutChangingSubscription()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        await SeedSubscriptionAsync(user.Id);
        using var client = _factory.CreateAuthenticatedClient(key);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Action"] = "pause", ["_handler"] = "billing-pause" });

        using var response = await client.PostAsync(RoutePaths.AccountBilling, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await db.Subscriptions.AsNoTracking().SingleAsync(s => s.UserId == user.Id);
        Assert.Null(subscription.PausedAt);
    }

    [Fact]
    public async Task PauseAndResume_UpdatesWorkspaceSeatPlan()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey, PlanTierLimits.Pro);
        var member = await _factory.SeedUserAsync(Guid.CreateVersion7().ToString(), PlanTierLimits.Pro);
        await SeedSubscriptionAsync(owner.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var workspace = new Workspace
            {
                Id = Guid.CreateVersion7(),
                Name = "Billing test workspace",
                OwnerId = owner.Id,
            };
            db.Workspaces.Add(workspace);
            db.WorkspaceMembers.Add(new WorkspaceMember
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = workspace.Id,
                UserId = member.Id,
                InviteEmail = member.Email,
                JoinedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateAuthenticatedClient(ownerKey);
        var html = await client.GetStringAsync(RoutePaths.AccountBilling);
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
            ["_handler"] = "billing-pause",
            ["Action"] = "pause",
        }))
        {
            using var response = await client.PostAsync(RoutePaths.AccountBilling, form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(PlanTierLimits.Free, await db.Users.Where(u => u.Id == member.Id).Select(u => u.Plan).SingleAsync());
        }

        html = await client.GetStringAsync(RoutePaths.AccountBilling);
        using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
            ["_handler"] = "billing-resume",
            ["Action"] = "resume",
        }))
        {
            using var response = await client.PostAsync(RoutePaths.AccountBilling, form);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(PlanTierLimits.Pro, await db.Users.Where(u => u.Id == member.Id).Select(u => u.Plan).SingleAsync());
        }
    }

    private async Task SeedSubscriptionAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Subscriptions.Add(new Subscription
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Plan = PlanTierLimits.Pro,
            Status = SubscriptionStatus.Active,
            BillingPeriod = BillingPeriod.Monthly,
            CurrentPeriodStart = DateTime.UtcNow.AddDays(-10),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(20),
        });
        await db.SaveChangesAsync();
    }
}
