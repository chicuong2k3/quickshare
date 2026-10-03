using Microsoft.Playwright;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class WorkspaceAuditUnpolyBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;

    public WorkspaceAuditUnpolyBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Filter_UpdatesUrlAndKeepsPageWithoutReload()
    {
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountWorkspaceAudit);
        await Assertions.Expect(page.Locator("#workspace-audit-body")).ToBeVisibleAsync();
        await page.EvaluateAsync("window.__workspaceAuditMarker = true");

        await page.Locator("#ws-audit-action").SelectOptionAsync("WorkspaceCreated");
        await page.Locator("[data-ai-action='workspace.audit.filter']").ClickAsync();

        await page.WaitForURLAsync("**/account/workspace/audit?action=WorkspaceCreated*");
        await Assertions.Expect(page.Locator("#ws-audit-action")).ToHaveValueAsync("WorkspaceCreated");
        Assert.True(await page.EvaluateAsync<bool>("window.__workspaceAuditMarker === true"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Filter_WithoutJavascript_RemainsUsable()
    {
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountWorkspaceAudit);
        await Assertions.Expect(page.Locator("#workspace-audit-body")).ToBeVisibleAsync();

        await page.Locator("#ws-audit-action").SelectOptionAsync("WorkspaceCreated");
        await page.Locator("[data-ai-action='workspace.audit.filter']").ClickAsync();

        await page.WaitForURLAsync("**/account/workspace/audit?action=WorkspaceCreated*");
        await Assertions.Expect(page.Locator("#ws-audit-action")).ToHaveValueAsync("WorkspaceCreated");
        await Assertions.Expect(page.Locator("#workspace-audit-body")).ToBeVisibleAsync();
    }
}
