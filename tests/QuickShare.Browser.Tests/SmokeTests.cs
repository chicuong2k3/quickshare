using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Browser smoke tests: real Chromium against real Kestrel.
/// Covers what the in-process tier cannot — JS execution, HTMX swaps, WebRTC signaling.
/// </summary>
public sealed class SmokeTests : IClassFixture<BrowserFixture>
{
    private static readonly string[] IgnoredErrorFragments =
    [
        "daub.dev", // external analytics script, unreachable offline
    ];

    private readonly BrowserFixture _fx;

    public SmokeTests(BrowserFixture fx) => _fx = fx;

    private static List<string> TrackPageErrors(IPage page)
    {
        var errors = new List<string>();
        page.PageError += (_, error) =>
        {
            if (!IgnoredErrorFragments.Any(error.Contains))
                errors.Add(error);
        };
        return errors;
    }

    [Theory]
    [InlineData("/", "QuickShareCloud")]
    [InlineData("/pricing", "QuickShareCloud")]
    [InlineData("/p2p/send", "QuickShareCloud")]
    public async Task PublicPage_RendersWithoutJsErrors(string path, string expectedTitle)
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = TrackPageErrors(page);

        await page.GotoAsync(_fx.BaseUrl + path);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        Assert.Contains(expectedTitle, await page.TitleAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task UpgradePage_HtmxLazyLoad_FillsSkeletonWithContent()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = TrackPageErrors(page);

        // Mark the window; if HTMX swaps (no full reload) the marker survives.
        await page.GotoAsync(_fx.BaseUrl + "/upgrade?plan=pro");
        await page.EvaluateAsync("window.__htmxSmokeMarker = true");

        // The lazy-load skeleton fires an hx-get on load; wait for the swapped-in content.
        await page.WaitForSelectorAsync("text=/QSC-|Pro|checkout|thanh toán/i",
            new PageWaitForSelectorOptions { Timeout = 10_000 });

        var markerSurvived = await page.EvaluateAsync<bool>("window.__htmxSmokeMarker === true");
        Assert.True(markerSurvived, "Full page reload happened — HTMX swap expected");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task P2PSend_SelectFile_GeneratesShareLink()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        var errors = TrackPageErrors(page);

        await page.GotoAsync(_fx.BaseUrl + "/p2p/send");

        await page.SetInputFilesAsync("#p2ps-file-input", new FilePayload
        {
            Name = "hello.txt",
            MimeType = "text/plain",
            Buffer = System.Text.Encoding.UTF8.GetBytes("hello p2p"),
        });

        // send.js posts /api/shares/p2p and fills #p2ps-share-link with the link.
        var linkInput = page.Locator("#p2ps-share-link");
        await Assertions.Expect(linkInput).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("/p2p/d/[0-9a-f]+"),
            new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });

        Assert.Empty(errors);
    }
}
