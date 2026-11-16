using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Browser.Tests;

/// <summary>
/// The accessibility pass on the pages a stranger actually lands on (issue #95).
///
/// Not a scanner run — a scanner reports contrast ratios and duplicate ids, and would not have
/// caught any of what this batch fixed. These assert the three things a keyboard or screen-reader
/// user needs and that nothing else in the suite checks: a way past the navbar, a name on every
/// control, and errors that are *announced* rather than merely displayed.
/// </summary>
public sealed class AccessibilityTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fx;

    public AccessibilityTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task SkipLink_IsTheFirstStopAndLandsOnTheContent()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/");

        // First Tab from the top of the document must reach it, or it is not a bypass.
        await page.Keyboard.PressAsync("Tab");
        var focusedClass = await page.EvaluateAsync<string>("document.activeElement.className");
        Assert.Contains("skip-link", focusedClass, StringComparison.Ordinal);

        await page.Keyboard.PressAsync("Enter");

        // And it has somewhere to land: the landmark, not a fragment that does not exist.
        await Assertions.Expect(page.Locator("#main-content")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Layout_ExposesOneMainLandmark()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/");

        // "Jump to main" is the first thing every screen-reader user tries.
        Assert.Equal(1, await page.Locator("main#main-content").CountAsync());
    }

    [Fact]
    public async Task NavbarControls_AllHaveAccessibleNames()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/");

        var unnamed = await page.EvaluateAsync<int>("""
            Array.from(document.querySelectorAll('nav button, nav a'))
                .filter(el => el.offsetParent !== null)
                .filter(el => !(el.textContent || '').trim() && !el.getAttribute('aria-label') && !el.getAttribute('title'))
                .length
            """);

        // An icon button with no name is announced as "button" and nothing else.
        Assert.Equal(0, unnamed);
    }

    [Fact]
    public async Task OtpBoxes_EachHaveTheirOwnName()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        // A real delivery: the gate phases only render for a share that exists.
        await page.GotoAsync(_fx.BaseUrl + "/d/" + await SeedDeliveryAsync());

        var names = await page.EvaluateAsync<string[]>("""
            Array.from(document.querySelectorAll('#dl-otp input')).map(el => el.getAttribute('aria-label') || '')
            """);

        // Six identical unlabelled boxes are six "edit text" announcements and an unenterable
        // code; each needs to say which digit it is.
        Assert.Equal(6, names.Length);
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
        Assert.Equal(6, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task GateErrors_AreAnnouncedNotJustShown()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/d/" + await SeedDeliveryAsync());

        // A wrong password that is only *displayed* is a dead end for anybody not looking at
        // that spot on the screen.
        Assert.Equal("alert", await page.GetAttributeAsync("#dl-password-error", "role"));
        Assert.Equal("alert", await page.GetAttributeAsync("#dl-otp-error", "role"));
    }

    [Fact]
    public async Task ControlNames_FollowTheChosenLanguage()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/");

        await page.SelectOptionAsync(".lang-switch select", "vi");
        await Assertions.Expect(page.Locator(".skip-link")).ToContainTextAsync("nội dung",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        // The labels that were hardcoded English in a bilingual product: a Vietnamese
        // screen-reader user was being read "Menu"/"Legal"/"User menu" regardless.
        var menuLabel = await page.GetAttributeAsync(".db-navbar__toggle", "aria-label");
        Assert.NotNull(menuLabel);

        await page.SelectOptionAsync(".lang-switch select", "en");
        await Assertions.Expect(page.Locator(".skip-link")).ToContainTextAsync("Skip",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }

    /// <summary>A live share, so the page renders its gate phases rather than the not-found one.</summary>
    private async Task<string> SeedDeliveryAsync()
    {
        var shareCode = $"a11y-{Guid.NewGuid():N}"[..12];

        using var scope = _fx.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "accessible.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            TotalBytes = 2048,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();

        return shareCode;
    }
}
