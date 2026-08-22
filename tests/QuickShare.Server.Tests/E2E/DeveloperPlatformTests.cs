using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Developer;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Features.Developer.Webhooks;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The developer platform: API keys (#115), outbound webhooks (#39), signing and retry (#172),
/// the event log (#223) and replay (#288).
///
/// The properties worth holding, and what breaks if they do not:
///
/// - **A credential is returned once and stored as a hash.** A key a support agent can read back
///   is a key an attacker can social-engineer.
/// - **The signature covers the timestamp, not just the body.** Sign only the body and a captured
///   request stays valid forever, with no way for the receiver to tell a replay from the original.
/// - **A failed delivery retries with backoff and is then kept.** The usual cause is the receiver
///   being down; hammering it makes that worse, and discarding the event makes it unrecoverable.
/// - **Privacy mode does not emit webhooks.** A share that keeps no record must not export one.
/// </summary>
public sealed class DeveloperPlatformTests : IClassFixture<TestWebAppFactory>
{
    private const string HookUrl = "https://hooks.example/quickshare";

    private readonly TestWebAppFactory _factory;

    public DeveloperPlatformTests(TestWebAppFactory factory) => _factory = factory;

    // ── #115 API keys ──

    [Fact]
    public async Task ApiKey_IsReturnedOnceAndStoredHashed()
    {
        var (client, userId) = await AccountAsync();

        var created = await client.PostAsJsonAsync("/api/developer/keys", new { name = "CI", scopes = "read,write" });
        created.EnsureSuccessStatusCode();
        var key = (await created.Content.ReadFromJsonAsync<ApiKeyDto>())!;

        Assert.False(string.IsNullOrEmpty(key.Key));
        Assert.StartsWith(ApiKey.TokenPrefix, key.Key, StringComparison.Ordinal);

        // Listing never returns it again.
        var listed = await client.GetFromJsonAsync<List<ApiKeyDto>>("/api/developer/keys");
        Assert.All(listed!, k => Assert.Null(k.Key));

        // And the row holds a hash, not the key.
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.ApiKeys.AsNoTracking().FirstAsync(k => k.UserId == userId);

        Assert.NotEqual(key.Key, stored.KeyHash);
        Assert.Equal(ApiKey.HashLength, stored.KeyHash.Length);
        // The prefix identifies without enabling: it must not be enough to reconstruct the key.
        Assert.True(stored.Prefix.Length < key.Key!.Length);
    }

    [Fact]
    public async Task ApiKey_ResolvesItsOwnerAndStopsWhenRevoked()
    {
        var (client, userId) = await AccountAsync();
        var raw = await CreateKeyAsync(client);

        Assert.Equal(userId, await ResolveOwnerAsync(raw));

        var listed = await client.GetFromJsonAsync<List<ApiKeyDto>>("/api/developer/keys");
        var revoke = await client.DeleteAsync($"/api/developer/keys/{listed![0].Id}");
        revoke.EnsureSuccessStatusCode();

        // Revocation has to bite immediately — that is the entire point of having a revoke button.
        Assert.Null(await ResolveOwnerAsync(raw));
    }

    [Fact]
    public async Task ApiKey_RefusesAnExpiredKey()
    {
        var (client, _) = await AccountAsync();
        var raw = await CreateKeyAsync(client);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var key = await db.ApiKeys.FirstAsync(k => k.KeyHash == HashOf(raw));
            key.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        Assert.Null(await ResolveOwnerAsync(raw));
    }

    [Fact]
    public async Task ApiKey_CannotBeRevokedByAnotherAccount()
    {
        var (owner, _) = await AccountAsync();
        var (stranger, _) = await AccountAsync();

        await CreateKeyAsync(owner);
        var listed = await owner.GetFromJsonAsync<List<ApiKeyDto>>("/api/developer/keys");

        var response = await stranger.DeleteAsync($"/api/developer/keys/{listed![0].Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("read", "read")]
    [InlineData("READ, write", "read,write")]
    [InlineData("nonsense", "read")]
    [InlineData("", "read")]
    public void Scopes_NormaliseToTheKnownSet(string raw, string expected)
    {
        Assert.Equal(expected, ApiScopes.Normalize(raw));
    }

    // ── #172 signing ──

    [Fact]
    public void Signature_CoversTheTimestampNotJustTheBody()
    {
        const string Secret = "s3cret";
        const string Body = "{\"event\":\"delivery.created\"}";

        var atOne = ApiKeyAuthenticator.Sign(Secret, 1_700_000_000, Body);
        var atTwo = ApiKeyAuthenticator.Sign(Secret, 1_700_000_060, Body);

        // Identical bodies at different times must not share a signature. If they did, a captured
        // request would stay valid forever and the receiver could not tell a replay from the
        // original — which is the whole reason the timestamp is inside the signed material.
        Assert.NotEqual(atOne, atTwo);

        // Deterministic for the same inputs, or no receiver could verify it.
        Assert.Equal(atOne, ApiKeyAuthenticator.Sign(Secret, 1_700_000_000, Body));
        // And keyed: a different secret is a different signature.
        Assert.NotEqual(atOne, ApiKeyAuthenticator.Sign("other", 1_700_000_000, Body));
    }

    // ── #39 subscriptions ──

    [Fact]
    public async Task Webhook_RefusesPlaintextHttp()
    {
        var (client, _) = await AccountAsync();

        var response = await client.PostAsJsonAsync("/api/developer/webhooks",
            new { url = "http://hooks.example/insecure", events = WebhookEvents.DeliveryCreated });

        // A signed payload over plaintext is a payload anyone on the path can read, and the
        // events carry file names.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Webhook_ReturnsItsSecretOnceOnly()
    {
        var (client, _) = await AccountAsync();

        var created = await CreateHookAsync(client);
        Assert.False(string.IsNullOrEmpty(created.Secret));

        var listed = await client.GetFromJsonAsync<List<WebhookEndpointDto>>("/api/developer/webhooks");
        Assert.All(listed!, e => Assert.Null(e.Secret));
    }

    [Fact]
    public async Task Webhook_QueuesAnEventForASubscribedEndpoint()
    {
        var (client, userId) = await AccountAsync();
        await CreateHookAsync(client);

        await EnqueueAsync(userId, WebhookEvents.DeliveryCreated);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queued = await db.WebhookDeliveries.AsNoTracking()
            .Where(d => d.UserId == userId).ToListAsync();

        Assert.Single(queued);
        Assert.Equal(WebhookDeliveryStatus.Pending, queued[0].Status);
        Assert.Contains("delivery.created", queued[0].PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Webhook_IgnoresAnEventTheEndpointDidNotSubscribeTo()
    {
        var (client, userId) = await AccountAsync();
        await CreateHookAsync(client, events: WebhookEvents.DeliveryExpired);

        await EnqueueAsync(userId, WebhookEvents.DeliveryCreated);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.WebhookDeliveries.AsNoTracking().Where(d => d.UserId == userId).ToListAsync());
    }

    // ── #172 retry ──

    [Fact]
    public async Task Delivery_RetriesWithBackoffAndIsKeptWhenItGivesUp()
    {
        var (client, userId) = await AccountAsync();
        await CreateHookAsync(client);
        await EnqueueAsync(userId, WebhookEvents.DeliveryCreated);

        // hooks.example does not resolve, so every attempt fails the way a dead receiver would.
        for (var pass = 1; pass <= WebhookDelivery.MaxAttempts; pass++)
        {
            await RunJobAsync();
            await MakeDueAsync(userId);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.WebhookDeliveries.AsNoTracking().FirstAsync(d => d.UserId == userId);

        Assert.Equal(WebhookDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(WebhookDelivery.MaxAttempts, delivery.AttemptCount);
        // Kept, not deleted: this row is what replay (#288) sends again.
        Assert.False(string.IsNullOrEmpty(delivery.PayloadJson));
    }

    [Fact]
    public void Backoff_GrowsAndDoesNotRunAway()
    {
        var first = WebhookDelivery.BackoffFor(1);
        var second = WebhookDelivery.BackoffFor(2);
        var last = WebhookDelivery.BackoffFor(WebhookDelivery.MaxAttempts);

        Assert.True(second > first);
        // Capped: the point of backing off is to stop hammering a struggling receiver, not to
        // schedule a retry for next week.
        Assert.True(last <= TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Endpoint_ParksItselfAfterEnoughFailures()
    {
        var (client, userId) = await AccountAsync();
        await CreateHookAsync(client);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var endpoint = await db.WebhookEndpoints.FirstAsync(e => e.UserId == userId);
            endpoint.ConsecutiveFailures = WebhookEndpoint.MaxConsecutiveFailures;
            await db.SaveChangesAsync();

            // Retrying a dead URL forever fills the queue with work that will never succeed.
            Assert.True(endpoint.IsParked);
            Assert.False(endpoint.IsLive);
        }

        // Saving the endpoint clears the streak — fixing the receiver and pressing save is the
        // obvious gesture, and a separate "resume" control would be ceremony.
        var listed = await client.GetFromJsonAsync<List<WebhookEndpointDto>>("/api/developer/webhooks");
        var patched = await client.PatchAsJsonAsync($"/api/developer/webhooks/{listed![0].Id}", new { enabled = true });
        patched.EnsureSuccessStatusCode();

        await using var after = _factory.Services.CreateAsyncScope();
        var db2 = after.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(0, (await db2.WebhookEndpoints.AsNoTracking().FirstAsync(e => e.UserId == userId)).ConsecutiveFailures);
    }

    // ── #288 replay ──

    [Fact]
    public async Task Replay_SendsTheOriginalPayloadAndKeepsTheOriginalRow()
    {
        var (client, userId) = await AccountAsync();
        await CreateHookAsync(client);
        await EnqueueAsync(userId, WebhookEvents.DeliveryCreated);

        var log = await client.GetFromJsonAsync<List<WebhookDeliveryDto>>("/api/developer/webhooks/deliveries");
        var original = log!.Single();

        var replayed = await client.PostAsync($"/api/developer/webhooks/deliveries/{original.Id}/replay", null);
        replayed.EnsureSuccessStatusCode();
        var replay = (await replayed.Content.ReadFromJsonAsync<WebhookDeliveryDto>())!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.WebhookDeliveries.AsNoTracking().Where(d => d.UserId == userId).ToListAsync();

        // A new row, not a reset of the old one: the log records what happened, and rewriting
        // history to hide a failure is exactly what a debugging log must not do.
        Assert.Equal(2, rows.Count);
        Assert.NotEqual(original.Id, replay.Id);
        Assert.Equal(
            rows.Single(r => r.Id == original.Id).PayloadJson,
            rows.Single(r => r.Id == replay.Id).PayloadJson);
    }

    [Fact]
    public async Task Replay_CannotReachAnotherAccountsDelivery()
    {
        var (owner, ownerId) = await AccountAsync();
        var (stranger, _) = await AccountAsync();
        await CreateHookAsync(owner);
        await EnqueueAsync(ownerId, WebhookEvents.DeliveryCreated);

        var log = await owner.GetFromJsonAsync<List<WebhookDeliveryDto>>("/api/developer/webhooks/deliveries");

        var response = await stranger.PostAsync($"/api/developer/webhooks/deliveries/{log!.Single().Id}/replay", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── The screen ──

    [Fact]
    public async Task DeveloperPage_CarriesKeysHooksAndLog()
    {
        var (client, userId) = await AccountAsync();
        await CreateKeyAsync(client);
        await CreateHookAsync(client);
        await EnqueueAsync(userId, WebhookEvents.DeliveryCreated);

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountDeveloper);
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("dev.key.create", html, StringComparison.Ordinal);
        Assert.Contains("dev.hook.create", html, StringComparison.Ordinal);
        Assert.Contains("dev.log.replay", html, StringComparison.Ordinal);
        Assert.Contains(HookUrl, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeveloperPage_NeverRendersAKeyOrASecret()
    {
        var (client, _) = await AccountAsync();
        var raw = await CreateKeyAsync(client);
        var hook = await CreateHookAsync(client);

        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.AccountDeveloper);
        request.Headers.Add("HX-Request", "true");
        var html = await (await client.SendAsync(request)).Content.ReadAsStringAsync();

        // Both exist in exactly one response each. If either turns up in the page source it is in
        // the browser cache, the back button and every proxy log on the way.
        Assert.DoesNotContain(raw, html, StringComparison.Ordinal);
        Assert.DoesNotContain(hook.Secret!, html, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private static string HashOf(string raw) =>
        QuickShare.Features.Workspaces.Shared.WorkspaceService.HashToken(raw);

    private async Task<(HttpClient Client, Guid UserId)> AccountAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        return (_factory.CreateAuthenticatedClient(providerKey), user.Id);
    }

    private static async Task<string> CreateKeyAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/developer/keys", new { name = "CI", scopes = "read,write" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ApiKeyDto>())!.Key!;
    }

    private static async Task<WebhookEndpointDto> CreateHookAsync(HttpClient client, string? events = null)
    {
        var response = await client.PostAsJsonAsync("/api/developer/webhooks", new
        {
            url = HookUrl,
            events = events ?? string.Join(',', WebhookEvents.All),
            enabled = true,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<WebhookEndpointDto>())!;
    }

    /// <summary>Drives the dispatcher directly — the trigger sites have their own tests.</summary>
    private async Task EnqueueAsync(Guid userId, string eventType)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
        await dispatcher.EnqueueAsync(userId, eventType, new { test = true }, CancellationToken.None);
    }

    private async Task<Guid?> ResolveOwnerAsync(string raw)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var authenticator = scope.ServiceProvider.GetRequiredService<ApiKeyAuthenticator>();

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = $"Bearer {raw}";

        var key = await authenticator.ResolveAsync(context, CancellationToken.None);
        return key?.UserId;
    }

    private async Task RunJobAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var job = new WebhookDeliveryJob(
            scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
            scope.ServiceProvider.GetRequiredService<ILogger<WebhookDeliveryJob>>());
        await job.RunAsync();
    }

    /// <summary>Pulls the next attempt forward so the backoff does not have to be waited out.</summary>
    private async Task MakeDueAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        foreach (var pending in await db.WebhookDeliveries
                     .Where(d => d.UserId == userId && d.Status == WebhookDeliveryStatus.Pending)
                     .ToListAsync())
        {
            pending.NextAttemptAt = DateTime.UtcNow.AddSeconds(-1);
        }

        await db.SaveChangesAsync();
    }
}
