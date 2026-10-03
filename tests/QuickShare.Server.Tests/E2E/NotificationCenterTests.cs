using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;
using QuickShare.Querying;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// In-app notification center (issue #246): unread count drives the bell badge, the list
/// pages by cursor and filters by read state, mark-read/read-all work owner-scoped.
/// </summary>
public sealed class NotificationCenterTests : IClassFixture<TestWebAppFactory>
{
    private const string NotificationsApi = "/api/notifications";

    private readonly TestWebAppFactory _factory;

    public NotificationCenterTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record NotificationRow(
        Guid Id, string Type, string Title, string? LinkUrl, bool IsRead, DateTime CreatedAt);

    private static async Task SeedAsync(TestWebAppFactory factory, Guid userId, int count, bool isRead = false)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.Notifications.Add(new Notification
            {
                UserId = userId,
                Type = NotificationType.DeliveryDownloaded,
                Title = $"file-{i}.pdf",
                LinkUrl = $"/d/abc{i}",
                IsRead = isRead,
                CreatedAt = DateTime.UtcNow.AddSeconds(-i),
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ListNotifications_RequiresAuth()
    {
        var response = await _factory.CreateClient().GetAsync(NotificationsApi);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ListNotifications_ReturnsOwnOnly()
    {
        var mineKey = Guid.CreateVersion7().ToString();
        var theirsKey = Guid.CreateVersion7().ToString();
        var mine = await _factory.SeedUserAsync(mineKey);
        var theirs = await _factory.SeedUserAsync(theirsKey);
        await SeedAsync(_factory, mine.Id, 2);
        await SeedAsync(_factory, theirs.Id, 3);

        var client = _factory.CreateAuthenticatedClient(mineKey);
        var page = await client.GetFromJsonAsync<CursorPage<NotificationRow>>(NotificationsApi);

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task UnreadCount_ReflectsOnlyUnread()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 3, isRead: false);
        await SeedAsync(_factory, user.Id, 2, isRead: true);

        var client = _factory.CreateAuthenticatedClient(providerKey);
        var response = await client.GetFromJsonAsync<UnreadCountResponse>($"{NotificationsApi}/unread-count");

        Assert.NotNull(response);
        Assert.Equal(3, response.Count);
    }

    private sealed record UnreadCountResponse(int Count);

    [Fact]
    public async Task MarkRead_SetsReadAndDecrementsCount()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 1);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var page = await client.GetFromJsonAsync<CursorPage<NotificationRow>>(NotificationsApi);
        var id = Assert.Single(page!.Items).Id;

        var mark = await client.PostAsync($"{NotificationsApi}/{id}/read", content: null);
        Assert.Equal(HttpStatusCode.OK, mark.StatusCode);

        var after = await client.GetFromJsonAsync<UnreadCountResponse>($"{NotificationsApi}/unread-count");
        Assert.Equal(0, after!.Count);
    }

    [Fact]
    public async Task MarkRead_OtherUsersNotification_ReturnsNotFound()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var otherKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(otherKey);
        await SeedAsync(_factory, owner.Id, 1);

        var ownerClient = _factory.CreateAuthenticatedClient(ownerKey);
        var page = await ownerClient.GetFromJsonAsync<CursorPage<NotificationRow>>(NotificationsApi);
        var id = Assert.Single(page!.Items).Id;

        var otherClient = _factory.CreateAuthenticatedClient(otherKey);
        var response = await otherClient.PostAsync($"{NotificationsApi}/{id}/read", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MarkAllRead_ClearsUnreadCount()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 4, isRead: false);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var markAll = await client.PostAsync($"{NotificationsApi}/read-all", content: null);
        Assert.Equal(HttpStatusCode.OK, markAll.StatusCode);

        var count = await client.GetFromJsonAsync<UnreadCountResponse>($"{NotificationsApi}/unread-count");
        Assert.Equal(0, count!.Count);
    }

    [Fact]
    public async Task Page_ReadAllForm_PostsWithoutJavascriptAndRedirects()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 2);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        var html = await client.GetStringAsync(RoutePaths.AccountNotificationCenter);
        var token = AntiforgeryHelper.ExtractToken(html);
        Assert.Contains("data-notification-read-all", html);

        var response = await client.PostAsync(RoutePaths.AccountNotificationReadAll,
            new FormUrlEncodedContent([
                new KeyValuePair<string, string>("__RequestVerificationToken", token),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        Assert.Contains("notifications-body", result);
        Assert.Contains("file-0.pdf", result);
        Assert.Contains("notif--read", result);
        Assert.DoesNotContain("notif--unread", result);
        var unreadCount = await client.GetFromJsonAsync<UnreadCountResponse>($"{NotificationsApi}/unread-count");
        Assert.Equal(0, unreadCount!.Count);
    }

    [Fact]
    public async Task Page_ReadAllForm_AnonymousCannotMutate()
    {
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var response = await client.PostAsync(
            RoutePaths.AccountNotificationReadAll, new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(RoutePaths.Login, response.Headers.Location?.AbsolutePath);
    }

    [Fact]
    public async Task Page_ReadAllForm_WithoutAntiforgeryToken_DoesNotWrite()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 1);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsync(RoutePaths.AccountNotificationReadAll,
            new FormUrlEncodedContent([]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var unreadCount = await client.GetFromJsonAsync<UnreadCountResponse>($"{NotificationsApi}/unread-count");
        Assert.Equal(1, unreadCount!.Count);
    }

    [Fact]
    public async Task ListFilter_UnreadOnly()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 2, isRead: false);
        await SeedAsync(_factory, user.Id, 3, isRead: true);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var page = await client.GetFromJsonAsync<CursorPage<NotificationRow>>($"{NotificationsApi}?filter=unread");

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, i => Assert.False(i.IsRead));
    }

    [Fact]
    public async Task Page_DirectGet_RendersNotificationsWithoutJavascript()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 1);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.GetAsync(RoutePaths.AccountNotificationCenter);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"notifications-body\"", html);
        Assert.Contains("notif__message", html);
        Assert.Contains("file-0.pdf", html);
        Assert.Contains("up-target=\"#notifications-body\"", html);
    }

    [Fact]
    public async Task Page_UnpolyFilter_ContainsOnlyUnreadNotifications()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 1);
        await SeedAsync(_factory, user.Id, 2, isRead: true);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{RoutePaths.AccountNotificationCenter}?filter=unread");
        request.Headers.Add(UnpolyHeaders.Target, "#notifications-body");

        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("file-0.pdf", html);
        Assert.DoesNotContain("file-1.pdf", html);
        Assert.Contains("id=\"notifications-body\"", html);
    }

    [Fact]
    public async Task Page_CursorDirectGet_RendersBookmarkablePageAndNextLink()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedAsync(_factory, user.Id, 35);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        var first = await client.GetFromJsonAsync<CursorPage<NotificationRow>>(NotificationsApi);
        Assert.NotNull(first?.NextCursor);

        var firstHtml = await client.GetStringAsync(RoutePaths.AccountNotificationCenter);
        Assert.Contains("up-defer=\"reveal\"", firstHtml);
        Assert.Contains("id=\"notifications-next-page\"", firstHtml);

        var cursorUrl = $"{RoutePaths.AccountNotificationCenter}?cursor={Uri.EscapeDataString(first.NextCursor)}";
        var next = await client.GetAsync(cursorUrl);
        var nextHtml = await next.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        Assert.Contains("id=\"notifications-pages\"", nextHtml);
        Assert.Contains("id=\"notifications-next-page\"", nextHtml);
        Assert.Contains("notif__message", nextHtml);
    }
}
