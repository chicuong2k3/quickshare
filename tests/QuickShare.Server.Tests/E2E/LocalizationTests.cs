using System.Net;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// vi/en localization (issue #9): culture cookie switches the UI language
/// and no raw resource keys leak into rendered pages.
/// </summary>
public sealed class LocalizationTests : IClassFixture<TestWebAppFactory>
{
    private const string CultureCookieVi = ".AspNetCore.Culture=c=vi|uic=vi";
    private const string HomeTitleViFragment = "gửi file, không phiền phức";
    private const string HomeTitleEnFragment = "send files, not friction";
    private const string PricingViFragment = "Bảng giá";

    private readonly TestWebAppFactory _factory;

    public LocalizationTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> GetHtmlAsync(string url, bool vi)
    {
        // HandleCookies=false — the default cookie container silently drops
        // a manually set Cookie header.
        var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (vi)
            request.Headers.Add("Cookie", CultureCookieVi);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // Razor entity-encodes non-ASCII (e.g. &#x1EED;) — decode before asserting.
        return WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task HomePage_ViCultureCookie_RendersVietnamese()
    {
        var html = await GetHtmlAsync("/", vi: true);
        Assert.Contains(HomeTitleViFragment, html);
    }

    [Fact]
    public async Task PricingPage_ViCultureCookie_RendersVietnamese()
    {
        var html = await GetHtmlAsync("/pricing", vi: true);
        Assert.Contains(PricingViFragment, html);
    }

    [Fact]
    public async Task HomePage_DefaultCulture_RendersEnglish()
    {
        var html = await GetHtmlAsync("/", vi: false);
        Assert.Contains(HomeTitleEnFragment, html);
        Assert.DoesNotContain(HomeTitleViFragment, html);
    }

    [Theory]
    [InlineData("/", false)]
    [InlineData("/", true)]
    [InlineData("/pricing", false)]
    [InlineData("/pricing", true)]
    public async Task Page_NoRawResourceKeysLeak(string url, bool vi)
    {
        var html = await GetHtmlAsync(url, vi);
        // A raw key showing up in the page body means a missing translation.
        Assert.DoesNotContain(">nav_pricing<", html);
        Assert.DoesNotContain(">home_h1_send<", html);
        Assert.DoesNotContain(">pricing_h1<", html);
    }
}
