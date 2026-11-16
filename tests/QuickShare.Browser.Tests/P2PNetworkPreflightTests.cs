using Microsoft.Playwright;

namespace QuickShare.Browser.Tests;

/// <summary>
/// Network preflight (issue #5): peers exchange the public IPv4 seen by STUN
/// before ICE, so a cross-network pair fails immediately instead of waiting out
/// the ICE timeout. Two contexts on one machine always share a public address,
/// so the mismatch path is driven through the exported comparison helpers
/// rather than by faking NAT.
/// </summary>
public sealed class P2PNetworkPreflightTests : IClassFixture<BrowserFixture>
{
    private const int ProbeTimeoutMs = 15_000;

    private readonly BrowserFixture _fx;

    public P2PNetworkPreflightTests(BrowserFixture fx) => _fx = fx;

    private async Task<IPage> OpenSendPageAsync()
    {
        var context = await _fx.CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fx.BaseUrl + "/p2p/send");
        await page.WaitForFunctionAsync("() => window.P2P && window.P2P.compareNetwork",
            null, new PageWaitForFunctionOptions { Timeout = ProbeTimeoutMs });
        return page;
    }

    [Theory]
    // Same public address on both sides — one NAT, transfer can work.
    [InlineData("203.0.113.7", "203.0.113.7", "same")]
    // Different public addresses — no direct path exists, fail up front.
    [InlineData("203.0.113.7", "198.51.100.4", "different")]
    // Either side unknown (STUN blocked, IPv6-only, timeout) must never block.
    [InlineData(null, "203.0.113.7", "unknown")]
    [InlineData("203.0.113.7", null, "unknown")]
    [InlineData(null, null, "unknown")]
    public async Task CompareNetwork_ClassifiesAddressPair(string? local, string? remote, string expected)
    {
        var page = await OpenSendPageAsync();

        var result = await page.EvaluateAsync<string>(
            "([a, b]) => window.P2P.compareNetwork(a, b)",
            new object?[] { local, remote });

        Assert.Equal(expected, result);
        Assert.Equal(expected, await page.EvaluateAsync<string>($"() => window.P2P.NETWORK_RESULT.{ResultKey(expected)}"));
    }

    private static string ResultKey(string result) => result;

    [Fact]
    public async Task ProbePublicAddress_ResolvesToIpv4OrNull_NeverRejects()
    {
        var page = await OpenSendPageAsync();

        // STUN may be unreachable on a build agent. Either outcome is valid; what
        // must never happen is a rejected promise, which would break the caller's
        // .then() chain and leave the UI stuck on "Connecting…".
        var probed = await page.EvaluateAsync<string?>(
            "() => window.P2P.probePublicAddress().then(function (a) { return a; }, function () { return 'REJECTED'; })",
            null);

        Assert.NotEqual("REJECTED", probed);
        if (probed is not null)
        {
            Assert.Matches(@"^\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}$", probed);
        }
    }
}
