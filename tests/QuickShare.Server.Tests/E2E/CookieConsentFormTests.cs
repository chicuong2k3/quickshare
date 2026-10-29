using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class CookieConsentFormTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public CookieConsentFormTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData(AdConstants.ConsentCookieValueGranted)]
    [InlineData(AdConstants.ConsentCookieValueDenied)]
    public async Task ConsentForm_SetsCookieAndReturnsToLocalPage(string consent)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync(RoutePaths.Home);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{RoutePaths.CookieConsent}?consent={consent}");
        request.Headers.Referrer = new Uri(client.BaseAddress!, "/pricing?billing=yearly");
        request.Content = TokenForm(html);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal("/pricing?billing=yearly", response.Headers.Location?.OriginalString);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.Contains($"{AdConstants.ConsentCookieName}={consent}"));
    }

    [Fact]
    public async Task ConsentForm_RejectsExternalRedirect()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var html = await client.GetStringAsync(RoutePaths.Home);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{RoutePaths.CookieConsent}?consent={AdConstants.ConsentCookieValueDenied}");
        request.Headers.Referrer = new Uri("https://example.com/path");
        request.Content = TokenForm(html);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal(RoutePaths.Home, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ConsentBanner_RendersNativeForms()
    {
        using var client = _factory.CreateClient();
        var html = await client.GetStringAsync(RoutePaths.Home);

        Assert.Contains($"action=\"{RoutePaths.CookieConsent}?consent={AdConstants.ConsentCookieValueGranted}\"", html);
        Assert.Contains($"action=\"{RoutePaths.CookieConsent}?consent={AdConstants.ConsentCookieValueDenied}\"", html);
        Assert.NotEmpty(AntiforgeryHelper.ExtractToken(html));
    }

    [Fact]
    public async Task ConsentForm_RejectsMissingAntiforgeryToken()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.PostAsync($"{RoutePaths.CookieConsent}?consent={AdConstants.ConsentCookieValueGranted}", new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith($"{AdConstants.ConsentCookieName}=", StringComparison.Ordinal)));
    }

    private static FormUrlEncodedContent TokenForm(string html) => new(new Dictionary<string, string>
    {
        ["__RequestVerificationToken"] = AntiforgeryHelper.ExtractToken(html),
    });
}
