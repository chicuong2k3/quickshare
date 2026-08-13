using Microsoft.Playwright;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Browser coverage for Quick Paste (#13). The paste plumbing lives entirely in
/// the browser — clipboard events, sessionStorage handoff, the editable-target
/// guard — so no server test can reach it.
///
/// Upload completion is deliberately out of scope: multipart needs R2, and the
/// local disk provider cannot serve it. These assert the paste path up to the
/// point the upload starts, which is the part this feature owns.
/// </summary>
public sealed class PasteBrowserTests : IClassFixture<BrowserAppFixture>
{
    private const string PastedTextMarker = "pasted-";

    private readonly BrowserAppFixture _fx;

    public PasteBrowserTests(BrowserAppFixture fx) => _fx = fx;

    /// <summary>Synthesises a real paste event carrying a text/plain payload.</summary>
    private const string PasteTextScript = """
        (text) => {
            const data = new DataTransfer();
            data.setData('text/plain', text);
            document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>Same, but the event targets a focused field instead of the document.</summary>
    private const string PasteIntoFieldScript = """
        (text) => {
            const field = document.querySelector('#upload-otp .db-otp__input');
            field.focus();
            const data = new DataTransfer();
            data.setData('text/plain', text);
            field.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>A 1x1 PNG delivered as a clipboard file item.</summary>
    private const string PasteImageScript = """
        () => {
            const b64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            const file = new File([bytes], 'clip.png', { type: 'image/png' });
            const data = new DataTransfer();
            data.items.add(file);
            document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    [Fact]
    public async Task PasteText_OnSend_StartsAnUploadNamedForThePaste()
    {
        await using var page = await NewPageAsync();
        await page.GotoAsync($"{_fx.BaseUrl}/send");

        await page.EvaluateAsync(PasteTextScript, "bien ban ban giao thiet bi");

        var filename = await WaitForTextAsync(page, "#upload-filename");
        Assert.Contains(PastedTextMarker, filename);
        Assert.EndsWith(".txt", filename);
    }

    [Fact]
    public async Task PasteImage_OnSend_StartsAnUploadNamedForThePaste()
    {
        await using var page = await NewPageAsync();
        await page.GotoAsync($"{_fx.BaseUrl}/send");

        await page.EvaluateAsync(PasteImageScript);

        var filename = await WaitForTextAsync(page, "#upload-filename");
        Assert.Contains(PastedTextMarker, filename);
        Assert.EndsWith(".png", filename);
    }

    [Fact]
    public async Task PasteOnHomepage_HandsOffToSendAndUploadsThere()
    {
        await using var page = await NewPageAsync();
        await page.GotoAsync($"{_fx.BaseUrl}/");

        await page.EvaluateAsync(PasteTextScript, "ghi chu tu trang chu");

        await page.WaitForURLAsync(u => u.Contains("/send"), new PageWaitForURLOptions { Timeout = 10_000 });

        var filename = await WaitForTextAsync(page, "#upload-filename");
        Assert.Contains(PastedTextMarker, filename);
    }

    [Fact]
    public async Task PasteIntoAField_IsNotHijacked()
    {
        await using var page = await NewPageAsync();
        await page.GotoAsync($"{_fx.BaseUrl}/send");

        await page.EvaluateAsync(PasteIntoFieldScript, "123456");

        // The idle panel must still be the visible one: no upload was started.
        await Task.Delay(700);
        Assert.False(await page.Locator("#upload-uploading").IsVisibleAsync());
        Assert.True(await page.Locator("#upload-idle").IsVisibleAsync());
    }

    /// <summary>
    /// The upload dies immediately here (multipart needs R2), so the uploading
    /// panel can be hidden again before an assertion runs — and InnerText of a
    /// hidden node is empty. TextContent reads the DOM regardless of visibility.
    /// </summary>
    private static async Task<string> WaitForTextAsync(IPage page, string selector)
    {
        var locator = page.Locator(selector);
        await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10_000 });

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var text = await locator.TextContentAsync() ?? string.Empty;
            if (text.Length > 0)
                return text;

            await Task.Delay(100);
        }

        return string.Empty;
    }

    private async Task<IPage> NewPageAsync()
    {
        // xunit v3 native skip: a machine without Chrome reports the gap
        // instead of failing a product test.
        if (_fx.Unavailable is not null)
            Assert.Skip(_fx.Unavailable);

        return await _fx.Browser.NewPageAsync();
    }
}
