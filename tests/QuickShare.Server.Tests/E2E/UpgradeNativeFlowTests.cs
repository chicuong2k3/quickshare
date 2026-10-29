using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class UpgradeNativeFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public UpgradeNativeFlowTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DirectGet_DoesNotCreatePaymentIntent()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);

        var html = await client.GetStringAsync(RoutePaths.UpgradePro);

        Assert.Contains("upgrade-promo", html);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PaymentIntents.AnyAsync(i => i.UserId == user.Id));
    }

    [Fact]
    public async Task CheckoutPost_PollsAndProvidesRefreshLink()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);
        var page = await client.GetStringAsync(RoutePaths.UpgradePro);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(page),
            ["_handler"] = "upgrade-checkout",
            ["Plan"] = "pro",
            ["Billing"] = "monthly",
        });
        using var response = await client.PostAsync(RoutePaths.UpgradePro, form);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("up-poll=\"true\"", html);
        Assert.Contains("Refresh payment status", html);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var intent = await db.PaymentIntents.AsNoTracking().SingleAsync(i => i.UserId == user.Id);
        var poll = await client.GetStringAsync($"{RoutePaths.UpgradePro}&orderCode={intent.PayOSOrderCode}&poll=1");
        Assert.Contains("Waiting for payment confirmation", poll);
        Assert.Equal(1, await db.PaymentIntents.CountAsync(i => i.UserId == user.Id));
    }

    [Fact]
    public async Task CheckoutPost_RejectsMissingCsrfToken()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string> { ["Plan"] = "pro", ["_handler"] = "upgrade-checkout" });

        using var response = await client.PostAsync(RoutePaths.UpgradePro, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PaymentIntents.AnyAsync(i => i.UserId == user.Id));
    }

    [Fact]
    public async Task CheckoutPost_RejectsUnknownFormWithoutCreatingIntent()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);
        var page = await client.GetStringAsync(RoutePaths.UpgradePro);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(page),
            ["_handler"] = "unknown-form",
            ["Plan"] = "pro",
        });

        using var response = await client.PostAsync(RoutePaths.UpgradePro, form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.PaymentIntents.AnyAsync(i => i.UserId == user.Id));
    }

    [Fact]
    public async Task TrialForm_StartsTrialWithoutJavascript()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key);
        using var client = _factory.CreateAuthenticatedClient(key);
        var page = await client.GetStringAsync(RoutePaths.UpgradePro);
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(page),
            ["_handler"] = "upgrade-trial",
            ["Action"] = "trial",
        });

        using var response = await client.PostAsync(RoutePaths.UpgradePro, form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(PlanTierLimits.Pro, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).Plan);
    }
}
