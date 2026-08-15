using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// The /send idle subtext must reflect auth state: anonymous visitors see the
/// "sign in to unlock higher limits" nudge, signed-in users see their plan's
/// per-file limit instead (regression: signed-in users were shown the
/// anonymous copy "200 MB per file · sign in to unlock higher limits").
/// </summary>
public sealed class UploadIdleSubtextBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fx;

    public UploadIdleSubtextBrowserTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task AnonymousVisitor_SeesSignInNudge()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(_fx.BaseUrl + "/send");

        await Assertions.Expect(page.Locator("#upload-idle-subtext"))
            .ToContainTextAsync("sign in to unlock");
    }

    [Fact]
    public async Task SignedInUser_SeesPlanLimit_NotSignInNudge()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();

        await page.GotoAsync(_fx.BaseUrl + "/send");

        await Assertions.Expect(page.Locator("#upload-idle-subtext"))
            .ToContainTextAsync("5 GB per file");
        await Assertions.Expect(page.Locator("#upload-idle-subtext"))
            .Not.ToContainTextAsync("sign in to unlock");
    }
}
