using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Browser smoke tests: real Chromium against real Kestrel.
/// Covers what the in-process tier cannot — JS execution, server-rendered swaps, WebRTC signaling.
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
    public async Task UpgradePage_DirectGet_ShowsCheckoutForm()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = TrackPageErrors(page);

        await page.GotoAsync(_fx.BaseUrl + "/upgrade?plan=pro");
        await page.Locator("#upgrade-promo").WaitForAsync();
        Assert.True(await page.Locator("#upgrade-body form").CountAsync() > 0);
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
