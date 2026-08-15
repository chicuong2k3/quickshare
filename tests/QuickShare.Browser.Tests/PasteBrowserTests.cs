using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Quick Paste (#13) in a real browser. The whole feature lives in the
/// browser — clipboard events, the sessionStorage handoff between pages, the
/// editable-target guard — so no server test can reach it.
///
/// Part PUTs go to http://stub.local/... (BrowserStorageStub), so these run
/// the paste all the way to a share link rather than stopping at the upload.
/// </summary>
public sealed class PasteBrowserTests : IClassFixture<BrowserFixture>
{
    private const string PartUrlGlob = "http://stub.local/**";
    private const string PastedName = "pasted-";

    private readonly BrowserFixture _fx;

    public PasteBrowserTests(BrowserFixture fx) => _fx = fx;

    /// <summary>Dispatches a real paste event carrying text.</summary>
    private const string PasteText = """
        (text) => {
            const data = new DataTransfer();
            data.setData('text/plain', text);
            document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>Same, but targeting a focused field rather than the document.</summary>
    private const string PasteIntoField = """
        (text) => {
            const field = document.querySelector('#upload-otp .db-otp__input');
            field.focus();
            const data = new DataTransfer();
            data.setData('text/plain', text);
            field.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    /// <summary>A 1x1 PNG delivered as a clipboard file item.</summary>
    private const string PasteImage = """
        () => {
            const b64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
            const bytes = Uint8Array.from(atob(b64), c => c.charCodeAt(0));
            const file = new File([bytes], 'clip.png', { type: 'image/png' });
            const data = new DataTransfer();
            data.items.add(file);
            document.dispatchEvent(new ClipboardEvent('paste', { clipboardData: data, bubbles: true, cancelable: true }));
        }
        """;

    private static Task FulfillPartPutAsync(IRoute route) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            Headers = new Dictionary<string, string> { ["ETag"] = "\"stub-etag\"" },
        });

    [Fact]
    public async Task PasteText_OnSend_UploadsAndReturnsAShareLink()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync(PartUrlGlob, FulfillPartPutAsync);

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.EvaluateAsync(PasteText, "bien ban ban giao thiet bi");

        await Assertions.Expect(page.Locator("#upload-success")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#upload-success-name")).ToContainTextAsync(PastedName);
        await Assertions.Expect(page.Locator("#upload-success-name")).ToContainTextAsync(".txt");
        await Assertions.Expect(page.Locator("#share-link")).ToHaveValueAsync(new Regex("/d/"));
    }

    [Fact]
    public async Task PasteImage_OnSend_UploadsAndReturnsAShareLink()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync(PartUrlGlob, FulfillPartPutAsync);

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.EvaluateAsync(PasteImage);

        await Assertions.Expect(page.Locator("#upload-success")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#upload-success-name")).ToContainTextAsync(PastedName);
        await Assertions.Expect(page.Locator("#upload-success-name")).ToContainTextAsync(".png");
    }

    [Fact]
    public async Task PasteOnHomepage_HandsOffThroughSessionStorage_AndFinishesOnSend()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync(PartUrlGlob, FulfillPartPutAsync);

        await page.GotoAsync(_fx.BaseUrl + "/");
        await page.EvaluateAsync(PasteText, "ghi chu tu trang chu");

        // Poll-based rather than event-based: the handoff navigation can commit before
        // WaitForURLAsync registers its listener, and an already-committed navigation
        // is never re-emitted — timing out on slow machines. ToHaveURL polls the URL.
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/send"), new PageAssertionsToHaveURLOptions { Timeout = 15_000 });

        await Assertions.Expect(page.Locator("#upload-success")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#upload-success-name")).ToContainTextAsync(PastedName);
    }

    [Fact]
    public async Task PasteIntoAField_IsNotHijacked()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.EvaluateAsync(PasteIntoField, "123456");

        // The idle panel stays up: pasting into an input must not start an upload.
        await Assertions.Expect(page.Locator("#upload-idle")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#upload-uploading")).Not.ToBeVisibleAsync();
    }
}
