using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.FileRequests.List;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class FileRequestsUnpolyBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;
    private const string RowPrefix = "browser-request-";

    public FileRequestsUnpolyBrowserTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task SeedAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "browser@quickshare.test");
        for (var index = 0; index <= FileRequestPaging.DefaultLimit; index++)
        {
            db.FileRequests.Add(new FileRequest
            {
                OwnerId = user.Id,
                Code = Guid.CreateVersion7().ToString("N")[..FileRequest.CodeMaxLength],
                Title = $"{RowPrefix}{index}",
                ExpiresAt = DateTime.UtcNow.AddDays(1),
                MaxFiles = 1,
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RevealNextPage_AppendsWithoutNavigation()
    {
        await SeedAsync();
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountFileRequests);
        await page.EvaluateAsync("window.__requestMarker = true");

        await page.Locator("#requests-next-page").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator("#requests-pages")).ToContainTextAsync(RowPrefix + 0);
        await Assertions.Expect(page.Locator("#requests-pages")).ToContainTextAsync(RowPrefix + FileRequestPaging.DefaultLimit);
        Assert.True(await page.EvaluateAsync<bool>("window.__requestMarker === true"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task WithoutJavascript_NextPageIsFollowable()
    {
        await SeedAsync();
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(_fixture.BaseUrl + RoutePaths.AccountFileRequests);
        await Assertions.Expect(page.Locator("#requests-pages")).ToContainTextAsync(RowPrefix);
        await page.Locator("#requests-next-page").ClickAsync();
        await page.WaitForURLAsync("**/account/requests?cursor=*");
        await Assertions.Expect(page.Locator("#requests-pages")).ToContainTextAsync(RowPrefix);
    }
}
