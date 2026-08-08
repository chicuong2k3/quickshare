using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Network-loss + resume in a real browser (issue #3). Part PUTs go to
/// http://stub.local/... (BrowserStorageStub), so Playwright routing plays
/// the network: abort = wifi down, fulfill 200+ETag = wifi back.
/// </summary>
public sealed class UploadResumeBrowserTests : IClassFixture<BrowserFixture>
{
    private const string PartUrlGlob = "http://stub.local/**";
    private const string FileInput = "input[type=file]";

    private readonly BrowserFixture _fx;

    public UploadResumeBrowserTests(BrowserFixture fx) => _fx = fx;

    private static Task FulfillPartPutAsync(IRoute route) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            Headers = new Dictionary<string, string> { ["ETag"] = "\"stub-etag\"" },
        });

    private static FilePayload SmallFile() => new()
    {
        Name = "resume-me.bin",
        MimeType = "application/octet-stream",
        Buffer = new byte[64 * 1024],
    };

    [Fact]
    public async Task NetworkLoss_ShowsResume_ResumeCompletesUpload()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        // Wifi down: every part PUT dies.
        await page.RouteAsync(PartUrlGlob, route => route.AbortAsync());

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.SetInputFilesAsync(FileInput, SmallFile());

        // 3 attempts with 1s/2s backoff exhaust → error screen with Resume.
        await Assertions.Expect(page.Locator("#upload-error")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#upload-resume-btn")).ToBeVisibleAsync();

        // Wifi back: PUTs succeed now.
        await page.UnrouteAsync(PartUrlGlob);
        await page.RouteAsync(PartUrlGlob, FulfillPartPutAsync);

        await page.ClickAsync("#upload-resume-btn");
        await Assertions.Expect(page.Locator("#upload-success")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#share-link")).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("/d/"));
    }

    [Fact]
    public async Task HealthyNetwork_UploadSucceedsFirstTry()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync(PartUrlGlob, FulfillPartPutAsync);

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.SetInputFilesAsync(FileInput, SmallFile());

        await Assertions.Expect(page.Locator("#upload-success")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 30_000 });
        await Assertions.Expect(page.Locator("#share-link")).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("/d/"));
    }

    [Fact]
    public async Task CancelMidUpload_ReturnsToIdle()
    {
        await using var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();

        // Never-resolving PUTs keep the upload in progress until cancel.
        await page.RouteAsync(PartUrlGlob, _ => Task.CompletedTask);

        await page.GotoAsync(_fx.BaseUrl + "/send");
        await page.SetInputFilesAsync(FileInput, SmallFile());

        await Assertions.Expect(page.Locator("#upload-cancel-btn")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await page.ClickAsync("#upload-cancel-btn");

        await Assertions.Expect(page.Locator("#upload-idle")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 10_000 });
        await Assertions.Expect(page.Locator("#upload-uploading")).ToBeHiddenAsync();
    }
}
