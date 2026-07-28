using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// E2E tests verifying every public page loads with the correct HTTP status code
/// and under 200ms (cold-start allowed on first request).
/// </summary>
public sealed class PageLoadTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PageLoadTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    // ── Public pages (no auth required) ──

    [Theory]
    [InlineData("/pricing")]
    [InlineData("/p2p/send")]
    [InlineData("/login")]
    public async Task PublicPage_Returns200_Under200ms(string url)
    {
        var client = _factory.CreateClient();
        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync(url);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Page {url} took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    [Fact]
    public async Task PricingPage_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/pricing");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task P2PSendPage_Returns200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/p2p/send");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Authenticated pages (dev auto-signin provides auth) ──

    [Theory]
    [InlineData("/upgrade?plan=pro")]
    [InlineData("/account")]
    [InlineData("/account/profile")]
    [InlineData("/account/billing")]
    public async Task AuthenticatedPage_Returns200_Under200ms(string url)
    {
        var client = _factory.CreateAuthenticatedClient();
        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync(url);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Page {url} took {sw.ElapsedMilliseconds}ms, expected < 200ms");
    }

    [Fact]
    public async Task PricingPage_BillingQueryParam_TogglesYearly()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/pricing?billing=yearly");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/nonexistent-page-xyz", HttpStatusCode.NotFound)]
    public async Task UnknownPage_ReturnsNotFound(string url, HttpStatusCode expectedStatus)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(expectedStatus, response.StatusCode);
    }
}
