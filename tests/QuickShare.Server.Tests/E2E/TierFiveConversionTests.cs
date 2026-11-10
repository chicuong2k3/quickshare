using System.Net;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Tier 5 conversion & distribution features:
/// - #130 wider share sheet (X, LinkedIn, Threads, Telegram, Email + copy Markdown/HTML)
/// - #135 live social proof on landing page
/// - #169 pricing calculator on pricing page
/// - #207 changelog RSS 2.0 feed + alternate link discovery
/// </summary>
public sealed class TierFiveConversionTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public TierFiveConversionTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UploadPage_RendersWiderShareSheetTargets_AndCopyButtons()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Send);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"upload-share-targets\"", html);
        Assert.Contains("id=\"upload-copy-markdown-btn\"", html);
        Assert.Contains("id=\"upload-copy-html-btn\"", html);
        Assert.Contains("id=\"upload-share-x\"", html);
        Assert.Contains("id=\"upload-share-linkedin\"", html);
        Assert.Contains("id=\"upload-share-threads\"", html);
        Assert.Contains("id=\"upload-share-telegram\"", html);
        Assert.Contains("id=\"upload-share-email\"", html);
    }

    [Fact]
    public async Task HomePage_RendersLiveSocialProofSection()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Home);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"home-social-proof\"", html);
        Assert.Contains("id=\"home-proof-deliveries\"", html);
        Assert.Contains("id=\"home-proof-volume\"", html);
        Assert.Contains("id=\"home-proof-e2e\"", html);
    }

    [Fact]
    public async Task PricingPage_RendersInteractivePricingCalculator()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync(RoutePaths.Pricing);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var html = await res.Content.ReadAsStringAsync();
        Assert.Contains("id=\"pricing-calculator\"", html);
        Assert.Contains("id=\"calc-files\"", html);
        Assert.Contains("id=\"calc-size-mb\"", html);
        Assert.Contains("id=\"calc-retention-days\"", html);
        Assert.Contains("id=\"calc-volume-out\"", html);
        Assert.Contains("id=\"calc-plan-out\"", html);
    }

    [Fact]
    public async Task ChangelogRss_ReturnsValidRssXml_AndChangelogHasDiscoveryLink()
    {
        var client = _factory.CreateClient();

        var rssRes = await client.GetAsync(RoutePaths.ChangelogRss);
        Assert.Equal(HttpStatusCode.OK, rssRes.StatusCode);
        Assert.Contains("application/rss+xml", rssRes.Content.Headers.ContentType?.ToString() ?? string.Empty);

        var xml = await rssRes.Content.ReadAsStringAsync();
        Assert.Contains("<rss version=\"2.0\">", xml);
        Assert.Contains("<title>QuickShareCloud Changelog</title>", xml);
        Assert.Contains("<item>", xml);

        var pageRes = await client.GetAsync(RoutePaths.Changelog);
        Assert.Equal(HttpStatusCode.OK, pageRes.StatusCode);
        var html = WebUtility.HtmlDecode(await pageRes.Content.ReadAsStringAsync());
        Assert.Contains("type=\"application/rss+xml\"", html);
        Assert.Contains(RoutePaths.ChangelogRss, html);
    }
}
