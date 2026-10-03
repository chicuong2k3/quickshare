using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class AccountReloadUnpolyBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public AccountReloadUnpolyBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(RoutePaths.AccountInvoices, "#invoices-body")]
    [InlineData(RoutePaths.AccountRecurring, "#recurring-body")]
    [InlineData(RoutePaths.AccountWorkspaceApprovals, "#workspace-approvals-body")]
    [InlineData(RoutePaths.AccountWorkspace, "#workspace-body")]
    [InlineData(RoutePaths.AccountWorkspaceDomain, "#workspace-domain-body")]
    [InlineData(RoutePaths.AccountWorkspaceBrand, "#workspace-brand-body")]
    [InlineData(RoutePaths.AccountWorkspaceChannel, "#workspace-channel-body")]
    [InlineData(RoutePaths.AccountWorkspaceStreaming, "#workspace-streaming-body")]
    [InlineData(RoutePaths.AccountWorkspaceBackup, "#workspace-backup-body")]
    public async Task ConvertedCard_ReloadsWithUnpolyWithoutPageNavigation(string route, string selector)
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(_fixture.BaseUrl + route);
        await Assertions.Expect(page.Locator(selector)).ToBeVisibleAsync();
        await page.EvaluateAsync("window.__accountReloadMarker = true");
        await page.EvaluateAsync("(selector) => { window.__accountReloadBody = document.querySelector(selector); }", selector);

        await page.EvaluateAsync("(selector) => window.qsPage.reload(selector)", selector);
        await page.WaitForFunctionAsync("(selector) => document.querySelector(selector) !== window.__accountReloadBody", selector);

        Assert.True(await page.EvaluateAsync<bool>("window.__accountReloadMarker === true"));
        Assert.Empty(errors);
    }
}
