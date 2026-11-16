using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class DeliveryFiltersBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public DeliveryFiltersBrowserTests(BrowserFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Filter_UpdatesFragmentAndUrlWithoutReloadingPage()
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountDeliveries);
        await Assertions.Expect(page.Locator("#deliveries-body")).ToBeVisibleAsync();
        await page.EvaluateAsync("window.__deliveryMarker = true; window.__deliveryBody = document.querySelector('#deliveries-body');");

        await page.Locator(".account-deliveries-filters a[href*='status=Active']").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#deliveries-body') !== window.__deliveryBody");

        Assert.Contains("status=Active", page.Url);
        Assert.True(await page.EvaluateAsync<bool>("window.__deliveryMarker === true"));
        Assert.Equal(0, await page.Locator("#deliveries-body [class*='db-']").CountAsync());
        var activeBackground = await page.Locator(".account-deliveries-filter--current")
            .EvaluateAsync<string>("element => getComputedStyle(element).backgroundColor");
        Assert.NotEqual("rgba(0, 0, 0, 0)", activeBackground);
    }
}
