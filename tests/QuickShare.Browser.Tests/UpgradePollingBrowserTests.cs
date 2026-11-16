using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class UpgradePollingBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public UpgradePollingBrowserTests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PendingPayment_PollsWithUnpoly()
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var polled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestCount = 0;
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("orderCode=1", StringComparison.Ordinal)
                && Interlocked.Increment(ref requestCount) >= 2)
            {
                polled.TrySetResult(true);
            }
        };

        await page.GotoAsync($"{_fixture.BaseUrl}{RoutePaths.UpgradePro}&orderCode=1&poll=1");
        await polled.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(await page.Locator("#payment-status").IsVisibleAsync());
    }

    [Fact]
    public async Task PendingPayment_OffersRefreshWithoutJavascript()
    {
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}{RoutePaths.UpgradePro}&orderCode=1&poll=1");

        var refresh = page.Locator("#payment-status a");
        await Assertions.Expect(refresh).ToBeVisibleAsync();
        Assert.Contains("orderCode=1", await refresh.GetAttributeAsync("href"));
    }
}
