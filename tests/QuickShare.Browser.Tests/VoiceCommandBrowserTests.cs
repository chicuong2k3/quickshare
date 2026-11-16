using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Voice commands (#277) in a real browser.
///
/// The whole feature lives in the browser, and the part worth testing is not the speech engine —
/// it is what happens *after* a phrase is recognised: that it maps onto the command palette's own
/// entries, that nothing runs before it is shown, and that it can be stopped. So the test injects
/// a fake <c>SpeechRecognition</c> and speaks into it.
/// </summary>
public sealed class VoiceCommandBrowserTests : IClassFixture<BrowserFixture>
{
    private const string StatusBar = "#voice-status";
    private const string Trigger = "#voice-trigger";

    /// <summary>
    /// A recognition engine that hears whatever the test says next. Installed before any script
    /// runs, so the page's own capability check sees it and keeps the mic button.
    /// </summary>
    private const string FakeSpeech = """
        window.__spoken = '';
        class FakeRecognition {
            start() {
                const phrase = window.__spoken;
                setTimeout(() => {
                    if (this.onresult) {
                        this.onresult({ results: [[{ transcript: phrase }]] });
                    }
                }, 0);
            }
        }
        // Both names: Chrome ships the webkit-prefixed one, and the page prefers whichever exists.
        window.SpeechRecognition = FakeRecognition;
        window.webkitSpeechRecognition = FakeRecognition;
        """;

    private readonly BrowserFixture _fx;

    public VoiceCommandBrowserTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task Speaking_APaletteCommand_ShowsItThenNavigates()
    {
        var page = await ListeningPageAsync();

        await page.EvaluateAsync("() => { window.__spoken = 'open pricing'; }");
        await page.ClickAsync(Trigger);

        // Shown before it runs — a mishearing that navigates silently is the failure mode.
        await Assertions.Expect(page.Locator(StatusBar)).ToContainTextAsync("Pricing",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await page.WaitForURLAsync("**/pricing", new PageWaitForURLOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Escape_StopsACommandBeforeItRuns()
    {
        var page = await ListeningPageAsync();

        await page.EvaluateAsync("() => { window.__spoken = 'open pricing'; }");
        await page.ClickAsync(Trigger);
        await Assertions.Expect(page.Locator(StatusBar)).ToContainTextAsync("Pricing",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });

        await page.Keyboard.PressAsync("Escape");

        // The undo for an action that has not happened yet is simply not doing it.
        await Assertions.Expect(page.Locator(StatusBar)).ToContainTextAsync("Cancelled",
            new LocatorAssertionsToContainTextOptions { Timeout = 5_000 });
        Assert.Contains("/", page.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("/pricing", page.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anythingelse_SaysSoRatherThanGuessing()
    {
        var page = await ListeningPageAsync();

        await page.EvaluateAsync("() => { window.__spoken = 'make me a sandwich'; }");
        await page.ClickAsync(Trigger);

        await Assertions.Expect(page.Locator(StatusBar)).ToContainTextAsync("No command matched",
            new LocatorAssertionsToContainTextOptions { Timeout = 10_000 });
    }

    [Fact]
    public async Task Matching_UsesTheSamePaletteEntriesTypingWould()
    {
        var page = await ListeningPageAsync();

        // The registry is the palette's list; voice is another way into the same menu.
        var href = await page.EvaluateAsync<string?>(
            "() => { const link = window.quickShareVoice.match('go to billing'); return link && link.getAttribute('href'); }");

        Assert.Equal("/account/billing", href);
    }

    [Fact]
    public async Task WithoutSpeechSupport_TheButtonIsHiddenRatherThanDead()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        // No fake engine installed: this is a browser that cannot listen.
        // defineProperty rather than delete: the native webkit-prefixed constructor is not
        // reliably deletable, and a half-removed engine would not be the browser being simulated.
        await page.AddInitScriptAsync("""
            Object.defineProperty(window, 'SpeechRecognition', { value: undefined, configurable: true });
            Object.defineProperty(window, 'webkitSpeechRecognition', { value: undefined, configurable: true });
            """);
        await page.GotoAsync(_fx.BaseUrl + "/");

        await Assertions.Expect(page.Locator(Trigger)).ToBeHiddenAsync(
            new LocatorAssertionsToBeHiddenOptions { Timeout = 10_000 });
    }

    private async Task<IPage> ListeningPageAsync()
    {
        var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.AddInitScriptAsync(FakeSpeech);
        await page.GotoAsync(_fx.BaseUrl + "/");
        await Assertions.Expect(page.Locator(Trigger)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });

        return page;
    }
}
