using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Real WebRTC P2P transfer between two browser contexts (issue #5):
/// sender creates a share, receiver opens the link, the file crosses a real
/// RTCDataChannel (signaling via the app's WebSocket middleware), and the
/// received byte count matches. Only NAT traversal needs real devices.
/// </summary>
public sealed class P2PTransferBrowserTests : IClassFixture<BrowserFixture>
{
    private const string FileName = "p2p-transfer.bin";
    private const int FileSize = 256 * 1024;

    private readonly BrowserFixture _fx;

    public P2PTransferBrowserTests(BrowserFixture fx) => _fx = fx;

    [Fact]
    public async Task TwoContexts_TransferFile_ReceivedSizeMatches()
    {
        // Sender
        await using var senderContext = await _fx.CreateContextAsync();
        var sender = await senderContext.NewPageAsync();
        await sender.GotoAsync(_fx.BaseUrl + "/p2p/send");

        var payload = new byte[FileSize];
        Random.Shared.NextBytes(payload);
        await sender.SetInputFilesAsync("#p2ps-file-input", new FilePayload
        {
            Name = FileName,
            MimeType = "application/octet-stream",
            Buffer = payload,
        });

        var linkInput = sender.Locator("#p2ps-share-link");
        await Assertions.Expect(linkInput).ToHaveValueAsync(
            new System.Text.RegularExpressions.Regex("/p2p/d/[0-9a-f]+"),
            new LocatorAssertionsToHaveValueOptions { Timeout = 15_000 });
        var shareLink = await linkInput.InputValueAsync();

        // Receiver — separate browser context = separate "device" (same machine, no NAT).
        await using var receiverContext = await _fx.CreateContextAsync();
        var receiver = await receiverContext.NewPageAsync();
        await receiver.GotoAsync(shareLink);

        await Assertions.Expect(receiver.Locator("#p2pd-ready")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });
        await Assertions.Expect(receiver.Locator("#p2pd-filename")).ToContainTextAsync(FileName);

        var download = await receiver.RunAndWaitForDownloadAsync(
            () => receiver.ClickAsync("#p2pd-dl-btn"),
            new PageRunAndWaitForDownloadOptions { Timeout = 60_000 });

        await Assertions.Expect(receiver.Locator("#p2pd-complete")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = 15_000 });

        var savedPath = await download.PathAsync();
        Assert.NotNull(savedPath);
        Assert.Equal(FileSize, new FileInfo(savedPath).Length);
    }
}
