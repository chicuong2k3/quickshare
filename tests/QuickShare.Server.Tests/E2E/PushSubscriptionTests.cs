using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Web push subscriptions (issue #103): the browser subscribes/unsubscribes against the
/// caller's account, and the VAPID public key is exposed for the client handshake.
/// </summary>
public sealed class PushSubscriptionTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PushSubscriptionTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record VapidKeyResponse(string Key);

    [Fact]
    public async Task VapidPublicKey_IsExposed()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.GetFromJsonAsync<VapidKeyResponse>("/api/push/vapid-public-key");

        Assert.NotNull(response);
        Assert.Equal("test-vapid-public-key", response.Key);
    }

    [Fact]
    public async Task Subscribe_SavesSubscription()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/push/subscribe", new
        {
            Endpoint = "https://push.example.com/sub/1",
            P256dh = "p256dh-key",
            Auth = "auth-key",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal("https://push.example.com/sub/1", sub.Endpoint);
    }

    [Fact]
    public async Task Subscribe_SameEndpoint_Upserts()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        await client.PostAsJsonAsync("/api/push/subscribe", new { Endpoint = "https://push.example.com/sub/2", P256dh = "old", Auth = "old" });
        await client.PostAsJsonAsync("/api/push/subscribe", new { Endpoint = "https://push.example.com/sub/2", P256dh = "new", Auth = "new" });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal("new", sub.P256dh);
    }

    [Fact]
    public async Task Unsubscribe_RemovesSubscription()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        await client.PostAsJsonAsync("/api/push/subscribe", new { Endpoint = "https://push.example.com/sub/3", P256dh = "p", Auth = "a" });

        Guid subId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            subId = (await db.PushSubscriptions.SingleAsync(s => s.UserId == user.Id)).Id;
        }

        var response = await client.DeleteAsync($"/api/push/unsubscribe/{subId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.PushSubscriptions.AnyAsync(s => s.UserId == user.Id));
        }
    }
}
