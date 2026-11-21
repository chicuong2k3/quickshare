using System.Text.RegularExpressions;
using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Destructive actions used to ask with window.confirm, which blocks the tab, dresses the
/// question in browser chrome nobody styled, and can be suppressed by the browser itself — a
/// delete that goes ahead because the prompt never appeared. Every [data-confirm] control now
/// opens the library's AlertDialog instead. This proves the wiring and both answers.
/// </summary>
public sealed class ConfirmDialogBrowserTests : IClassFixture<BrowserFixture>
{
    private const string KeyNameInput = "#dev-key-name";
    private const string CreateKeyButton = "[data-ai-action=\"dev.key.create\"]";
    private const string RevokeKeyButton = "[data-ai-action=\"dev.key.revoke\"]";
    private const string AlertDialog = "dialog[data-slot=\"alert-dialog\"][open]";
    private const string DialogYes = "[data-ask-yes]";
    private const string DialogNo = "[data-ask-no]";
    private const string KeyRow = "ul.workspace-member-list > li";
    private const string NewKeyHolder = "#dev-key-new";
    private const string DeleteAccountButton = "[data-ai-action=\"account.delete\"]";
    private const string PromptDialog = "dialog[data-slot=\"dialog\"][open]";
    private const string PromptInput = "dialog[data-slot=\"dialog\"][open] input[data-slot=\"input\"]";

    private const string DeleteAccountEndpoint = "/api/me/delete";
    private const string UiScriptPattern = @"/_content/Unpoly\.Blazor\.Shadcn/ui\.[a-z0-9]+\.js";
    private const string BareUiScript = "/_content/Unpoly.Blazor.Shadcn/ui.js";

    private const float VisibilityTimeoutMs = 10_000f;

    private readonly BrowserFixture _fx;

    public ConfirmDialogBrowserTests(BrowserFixture fx)
    {
        _fx = fx;
    }

    [Fact]
    public async Task DestructiveAction_AsksInLibraryDialog_AndHonoursBothAnswers()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();

        // A native confirm would fire this and the test would never see the click's effect.
        var nativeDialogs = new List<string>();
        page.Dialog += (_, dialog) =>
        {
            nativeDialogs.Add(dialog.Message);
            _ = dialog.DismissAsync();
        };

        await page.GotoAsync(_fx.BaseUrl + RoutePaths.AccountDeveloper);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        await page.FillAsync(KeyNameInput, "confirm dialog test");
        await page.ClickAsync(CreateKeyButton);

        // Creating a key only reveals its secret; the row that holds the Revoke control is
        // rendered by the section on the next load, so the test reloads like a reader would.
        await page.WaitForSelectorAsync(NewKeyHolder, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = VisibilityTimeoutMs,
        });
        await page.ReloadAsync();

        var revoke = page.Locator(RevokeKeyButton).First;
        await Assertions.Expect(revoke).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        var rowsBefore = await page.Locator(KeyRow).CountAsync();

        // No: the dialog closes and nothing is revoked.
        await revoke.ClickAsync();
        await Assertions.Expect(page.Locator(AlertDialog)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });
        await page.ClickAsync(DialogNo);
        await Assertions.Expect(page.Locator(AlertDialog)).ToHaveCountAsync(0);
        Assert.Equal(rowsBefore, await page.Locator(KeyRow).CountAsync());

        // Yes: the same dialog revokes, and the row goes away on the refreshed section.
        await page.Locator(RevokeKeyButton).First.ClickAsync();
        await Assertions.Expect(page.Locator(DialogYes)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });
        await page.Locator(DialogYes).ClickAsync();
        await Assertions.Expect(page.Locator(KeyRow)).ToHaveCountAsync(rowsBefore - 1);

        Assert.Empty(nativeDialogs);
    }

    /// <summary>
    /// The behaviour layer must arrive under a URL that changes when it changes. Served from the
    /// bare path, a reader's browser keeps last month's copy and every dialog added since then is
    /// missing for them — which is exactly how promptDialog went from "shipped" to "not a
    /// function" on one machine and not another.
    /// </summary>
    [Fact]
    public async Task BehaviourLayer_IsRequestedUnderAFingerprintedUrl()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();

        var scriptUrls = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) scriptUrls.Add(request.Url);
        };

        await page.GotoAsync(_fx.BaseUrl + RoutePaths.Home);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        Assert.Contains(scriptUrls, url => Regex.IsMatch(url, UiScriptPattern));
        Assert.DoesNotContain(BareUiScript, scriptUrls);
    }

    /// <summary>
    /// The delete-account control asks the reader to retype the address, which window.prompt did
    /// with an unlabelled OS box. Only the cancel and empty-answer paths are exercised: the point
    /// is that the field is the library's, and that leaving it blank still stops the caller
    /// before it posts a deletion.
    /// </summary>
    [Fact]
    public async Task TypedConfirmation_UsesLibraryDialog_AndAnEmptyAnswerCancels()
    {
        await using var context = await _fx.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();

        var requests = new List<string>();
        page.Request += (_, request) => requests.Add(request.Url);
        var nativeDialogs = new List<string>();
        page.Dialog += (_, dialog) =>
        {
            nativeDialogs.Add(dialog.Message);
            _ = dialog.DismissAsync();
        };

        await page.GotoAsync(_fx.BaseUrl + RoutePaths.Account);
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        var trigger = page.Locator(DeleteAccountButton).First;
        await Assertions.Expect(trigger).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });

        // Cancel: the dialog closes and nothing is posted.
        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator(PromptDialog)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });
        await Assertions.Expect(page.Locator(PromptInput)).ToBeFocusedAsync();
        await page.Locator(DialogNo).ClickAsync();
        await Assertions.Expect(page.Locator(PromptDialog)).ToHaveCountAsync(0);

        // Enter on an empty field resolves "", and the caller refuses to send it.
        await trigger.ClickAsync();
        await Assertions.Expect(page.Locator(PromptInput)).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = VisibilityTimeoutMs });
        await page.Locator(PromptInput).PressAsync("Enter");
        await Assertions.Expect(page.Locator(PromptDialog)).ToHaveCountAsync(0);

        Assert.DoesNotContain(requests, url => url.Contains(DeleteAccountEndpoint, StringComparison.Ordinal));
        Assert.Empty(nativeDialogs);
    }
}
