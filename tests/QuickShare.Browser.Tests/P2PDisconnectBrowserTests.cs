using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Disconnect visibility for a real WebRTC transfer. Covers the sender's receiver
/// list (which a scoped stylesheet used to keep permanently hidden) and both
/// mid-transfer departures: receiver leaves, and sender leaves.
/// </summary>
public sealed class P2PDisconnectBrowserTests : IClassFixture<BrowserFixture>
{
    private const string FileName = "p2p-disconnect.bin";
    // Large enough that the transfer is still running when a peer is closed:
    // 16 KB chunks are sent one per macrotask, so this spans several seconds.
    private const int FileSize = 24 * 1024 * 1024;
    private const int LinkTimeoutMs = 15_000;
    private const int ReadyTimeoutMs = 15_000;
    private const int StatusTimeoutMs = 30_000;

    private readonly BrowserFixture _fx;

    public P2PDisconnectBrowserTests(BrowserFixture fx) => _fx = fx;

    private sealed record Pair(IBrowserContext SenderContext, IPage Sender, string ShareLink);

    private async Task<Pair> StartShareAsync()
    {
        var senderContext = await _fx.CreateContextAsync();
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
            new LocatorAssertionsToHaveValueOptions { Timeout = LinkTimeoutMs });

        return new Pair(senderContext, sender, await linkInput.InputValueAsync());
    }

    private static async Task<IPage> OpenReceiverAsync(IBrowserContext context, string shareLink)
    {
        var receiver = await context.NewPageAsync();
        await receiver.GotoAsync(shareLink);
        await Assertions.Expect(receiver.Locator("#p2pd-ready")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = ReadyTimeoutMs });
        return receiver;
    }

    /// <summary>
    /// The list is built by send.js at runtime, so the styles that reveal it must
    /// live in the global sheet. A regression here makes the sender blind to every
    /// receiver state, which is exactly what this asserts against.
    /// </summary>
    [Fact]
    public async Task ReceiverJoins_SenderListBecomesVisible()
    {
        var pair = await StartShareAsync();
        await using var _ = pair.SenderContext;

        var list = pair.Sender.Locator("#p2ps-receiver-list");
        await Assertions.Expect(list).ToBeHiddenAsync();

        await using var receiverContext = await _fx.CreateContextAsync();
        await OpenReceiverAsync(receiverContext, pair.ShareLink);

        await Assertions.Expect(list).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = StatusTimeoutMs });
        await Assertions.Expect(pair.Sender.Locator(".p2p-receiver-entry")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task ReceiverLeavesMidTransfer_SenderShowsLeftMidTransfer()
    {
        var pair = await StartShareAsync();
        await using var _ = pair.SenderContext;

        await using var receiverContext = await _fx.CreateContextAsync();
        var receiver = await OpenReceiverAsync(receiverContext, pair.ShareLink);
        await receiver.ClickAsync("#p2pd-dl-btn");

        var status = pair.Sender.Locator(".p2p-receiver-status");
        await Assertions.Expect(status).ToHaveTextAsync("Receiving",
            new LocatorAssertionsToHaveTextOptions { Timeout = StatusTimeoutMs });

        await receiver.CloseAsync();

        await Assertions.Expect(status).ToHaveTextAsync("Left mid-transfer",
            new LocatorAssertionsToHaveTextOptions { Timeout = StatusTimeoutMs });
    }

    /// <summary>
    /// A sender that vanishes mid-transfer leaves an unfinishable download, so the
    /// receiver must surface an error with a retry — not fall back to the
    /// "waiting for sender" spinner, which reads as if the file were still coming.
    /// </summary>
    [Fact]
    public async Task SenderLeavesMidTransfer_ReceiverShowsError()
    {
        var pair = await StartShareAsync();

        await using var receiverContext = await _fx.CreateContextAsync();
        var receiver = await OpenReceiverAsync(receiverContext, pair.ShareLink);
        await receiver.ClickAsync("#p2pd-dl-btn");

        await Assertions.Expect(receiver.Locator("#p2pd-transfer")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = StatusTimeoutMs });

        await pair.SenderContext.CloseAsync();

        await Assertions.Expect(receiver.Locator("#p2pd-error")).ToBeVisibleAsync(
            new LocatorAssertionsToBeVisibleOptions { Timeout = StatusTimeoutMs });
        await Assertions.Expect(receiver.Locator("#p2pd-error-msg")).ToContainTextAsync("sender disconnected");
        await Assertions.Expect(receiver.Locator("#p2pd-retry")).ToBeVisibleAsync();
        await Assertions.Expect(receiver.Locator("#p2pd-waiting")).ToBeHiddenAsync();
    }
}
