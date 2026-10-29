using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class AccountFormsBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public AccountFormsBrowserTests(BrowserFixture fixture) => _fixture = fixture;

    private Task<IBrowserContext> ContextWithoutJavascriptAsync() => _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
    {
        JavaScriptEnabled = false,
        ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
    });

    [Fact]
    public async Task ProfileForm_ShowsValidationWithoutJavascript()
    {
        await using var context = await ContextWithoutJavascriptAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountProfile);
        await page.Locator("#name").FillAsync(" ");

        await page.Locator("#profile-card button[type=submit]").ClickAsync();

        await Assertions.Expect(page.Locator("#profile-card [role=alert]")).ToContainTextAsync("Name is required");
    }

    [Fact]
    public async Task CookieConsent_SavesWithoutJavascript()
    {
        await using var context = await ContextWithoutJavascriptAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.Home);
        await Assertions.Expect(page.Locator("#cookie-consent")).ToBeVisibleAsync();

        await page.Locator("#cookie-consent form[action*='consent=denied'] button").ClickAsync();

        await Assertions.Expect(page.Locator("#cookie-consent")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AccountDeletion_RequestsConfirmation()
    {
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
        });
        var page = await context.NewPageAsync();
        var dialogType = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Dialog += async (_, dialog) =>
        {
            dialogType.TrySetResult(dialog.Type);
            await dialog.DismissAsync();
        };
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.Account);

        await page.Locator("[data-ai-action='account.delete']").ClickAsync();

        Assert.Equal("prompt", await dialogType.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
