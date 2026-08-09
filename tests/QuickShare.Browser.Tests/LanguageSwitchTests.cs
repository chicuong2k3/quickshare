using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Culture switcher in a real browser (issue #9): selecting vi sets the culture
/// cookie via JS and reloads the page in Vietnamese; the choice sticks across navigation.
/// </summary>
public sealed class LanguageSwitchTests : IClassFixture<BrowserFixture>
{
    private const string LangSelect = ".lang-switch select";
    // nav_send: "Gửi" (vi) vs "Send" (en) — rendered in the layout on every page.
    private const string HomeViFragment = "Gửi";
    private const string HomeEnFragment = "Send";
    private const string PricingViFragment = "Bảng giá";

    private readonly BrowserFixture _fx;

    public LanguageSwitchTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task SelectVi_ReloadsInVietnamese_AndPersistsAcrossNavigation()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(_fx.BaseUrl + "/");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync(HomeEnFragment,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await page.SelectOptionAsync(LangSelect, "vi");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync(HomeViFragment,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Cookie persists: a different page is also Vietnamese.
        await page.GotoAsync(_fx.BaseUrl + "/pricing");
        await Assertions.Expect(page.Locator("body")).ToContainTextAsync(PricingViFragment,
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // Switch back to English.
        await page.SelectOptionAsync(LangSelect, "en");
        await Assertions.Expect(page.Locator(".lang-switch select")).ToHaveValueAsync("en",
            new LocatorAssertionsToHaveValueOptions { Timeout = 10_000 });
    }
}
