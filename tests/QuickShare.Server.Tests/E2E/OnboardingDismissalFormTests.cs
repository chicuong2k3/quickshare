using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using QuickShare.Features.Users.Onboarding;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class OnboardingDismissalFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public OnboardingDismissalFormTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task DirectPage_DismissesWithoutJavascriptAndRemainsDismissed()
    {
        var key = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(key);
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, key);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, TestWebAppFactory.TestUserEmail);
        client.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");

        var html = await client.GetStringAsync(RoutePaths.AccountOnboarding);
        Assert.Contains($"action=\"{RoutePaths.AccountOnboardingDismiss}\"", html);
        using (var missingToken = new FormUrlEncodedContent([]))
        {
            using var rejected = await client.PostAsync(RoutePaths.AccountOnboardingDismiss, missingToken);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }

        using (var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
        }))
        {
            using var response = await client.PostAsync(RoutePaths.AccountOnboardingDismiss, form);
            Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
            Assert.Equal(RoutePaths.Account, response.Headers.Location?.OriginalString);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains(OnboardingDismissal.CookieName));
        }

        var direct = await client.GetStringAsync(RoutePaths.AccountOnboarding);
        var account = await client.GetStringAsync(RoutePaths.Account);
        Assert.DoesNotContain("id=\"onboarding-card\"", direct);
        Assert.Contains("The getting started checklist is hidden on this browser", direct);
        Assert.DoesNotContain("id=\"onboarding-card\"", account);
    }

    [Fact]
    public async Task Dismissal_RequiresAuthentication()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsync(RoutePaths.AccountOnboardingDismiss, new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(response.Headers.TryGetValues("Set-Cookie", out var cookies) ? cookies : [], value => value.Contains(OnboardingDismissal.CookieName));
    }
}
