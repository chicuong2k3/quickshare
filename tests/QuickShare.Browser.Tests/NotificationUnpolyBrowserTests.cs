using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Browser.Tests;

public sealed class NotificationUnpolyBrowserTests : IClassFixture<BrowserFixture>
{
    private readonly BrowserFixture _fixture;
    private const string NotificationsRoute = "/account/notifications/inbox";
    private const string UnreadTitle = "browser-unread.pdf";
    private const string ReadTitle = "browser-read.pdf";

    public NotificationUnpolyBrowserTests(BrowserFixture fixture) => _fixture = fixture;

    private async Task SeedAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == "browser@quickshare.test");
        db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Type = NotificationType.DeliveryDownloaded,
            Title = UnreadTitle,
            IsRead = false,
        });
        db.Notifications.Add(new Notification
        {
            UserId = user.Id,
            Type = NotificationType.DeliveryDownloaded,
            Title = ReadTitle,
            IsRead = true,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task FilterAndMarkAll_SwapWithoutPageReload()
    {
        await SeedAsync();
        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(_fixture.BaseUrl + NotificationsRoute);
        await Assertions.Expect(page.Locator("#notifications-body")).ToContainTextAsync(UnreadTitle);
        await page.EvaluateAsync("window.__notificationMarker = true");

        await page.Locator(".notif-filters__item[href*='filter=unread']").ClickAsync();
        await page.WaitForURLAsync("**/account/notifications/inbox?filter=unread");
        await Assertions.Expect(page.Locator("#notifications-body")).Not.ToContainTextAsync(ReadTitle);
        Assert.True(await page.EvaluateAsync<bool>("window.__notificationMarker === true"));

        // BrowserFixture uses EF InMemory, which does not implement ExecuteUpdateAsync.
        // Server integration tests exercise the real API; stub its exact JSON shape here
        // while changing the same store so the subsequent Unpoly reload sees fresh data.
        var called = false;
        await page.RouteAsync("**/api/notifications/read-all", async route =>
        {
            called = true;
            using var scope = _fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == "browser@quickshare.test");
            var unread = await db.Notifications.Where(n => n.UserId == user.Id && !n.IsRead).ToListAsync();
            foreach (var notification in unread)
            {
                notification.IsRead = true;
            }
            await db.SaveChangesAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"read\":true}",
            });
        });

        await page.Locator(".notif-filters__markall").ClickAsync();
        await Assertions.Expect(page.Locator("#notifications-pages")).Not.ToContainTextAsync(UnreadTitle);
        Assert.True(called);
        Assert.True(await page.EvaluateAsync<bool>("window.__notificationMarker === true"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RevealNextPage_AppendsNotificationsWithoutReload()
    {
        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == "browser@quickshare.test");
            for (var i = 0; i < 35; i++)
            {
                db.Notifications.Add(new Notification
                {
                    UserId = user.Id,
                    Type = NotificationType.DeliveryDownloaded,
                    Title = $"browser-paged-{i}",
                    IsRead = false,
                });
            }
            await db.SaveChangesAsync();
        }

        await using var context = await _fixture.CreateContextAsync(authenticated: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(_fixture.BaseUrl + NotificationsRoute);
        await page.EvaluateAsync("window.__notificationMarker = true");

        await page.Locator("#notifications-next-page").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator("#notifications-pages")).ToContainTextAsync("browser-paged-0");
        await Assertions.Expect(page.Locator("#notifications-pages")).ToContainTextAsync("browser-paged-34");
        Assert.True(await page.EvaluateAsync<bool>("window.__notificationMarker === true"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DirectGetWithoutJavascript_RendersNotifications()
    {
        await SeedAsync();
        await using var context = await _fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            JavaScriptEnabled = false,
            ExtraHTTPHeaders = new Dictionary<string, string> { [TestAuthHandler.AuthHeader] = "1" },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(_fixture.BaseUrl + NotificationsRoute);

        await Assertions.Expect(page.Locator("#notifications-body")).ToContainTextAsync(UnreadTitle);
        await page.Locator(".notif-filters__item[href*='filter=unread']").ClickAsync();
        await page.WaitForURLAsync("**/account/notifications/inbox?filter=unread");
        await Assertions.Expect(page.Locator("#notifications-body")).ToContainTextAsync(UnreadTitle);
        await Assertions.Expect(page.Locator("#notifications-body")).Not.ToContainTextAsync(ReadTitle);
        await Assertions.Expect(page.Locator("[data-notification-read-all] button[type='submit']")).ToBeVisibleAsync();
    }
}
