using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Developer;
using QuickShare.Features.Developer.ApiKeys;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// API keys against the JSON API (issue #115).
///
/// Until this batch the keys authenticated nothing: they were issued, scoped, listed and
/// revocable, and every endpoint read the auth cookie. The scopes on the developer screen
/// described permissions nothing consulted. These tests are the proof that they now mean
/// something — including that a `read` key cannot write and a revoked one cannot do anything.
/// </summary>
/// <summary>One application for every API-key test class; see <see cref="ApiKeyUsageTests"/>.</summary>
[CollectionDefinition(ApiKeyTestGroup.Name)]
public sealed class ApiKeyTestGroup : ICollectionFixture<TestWebAppFactory>
{
    public const string Name = "api-key";
}

[Collection(ApiKeyTestGroup.Name)]
public sealed class ApiKeyAuthTests
{
    private const string FileName = "report.csv";
    private const string ContentType = "text/csv";
    private const long FileSize = 512;

    private readonly TestWebAppFactory _factory;

    public ApiKeyAuthTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WithoutAKey_TheApiIsStillClosed()
    {
        var response = await _factory.CreateClient().GetAsync("/api/deliveries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ReadKey_ReadsTheOwnersDeliveries()
    {
        var (owner, key) = await KeyAsync(ApiScopes.Read);
        var shareCode = await SeedDeliveryAsync(owner.Id);

        var response = await SendAsync(HttpMethod.Get, "/api/deliveries", key);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The key acts as its owner: the same rows the cookie would have shown, no more.
        Assert.Contains(shareCode, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadKey_CannotWrite()
    {
        var (_, key) = await KeyAsync(ApiScopes.Read);

        var response = await SendAsync(HttpMethod.Post, "/api/uploads/start-r2", key, new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(ErrorCodes.ApiKeyScopeRequired, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteKey_CompletesAnUploadEndToEnd()
    {
        var (owner, key) = await KeyAsync(ApiScopes.Read + "," + ApiScopes.Write);

        var startResponse = await SendAsync(HttpMethod.Post, "/api/uploads/start-r2", key, new StartR2Request
        {
            FileName = FileName,
            FileSize = FileSize,
            ContentType = ContentType,
            PartNumbers = [1],
        });
        startResponse.EnsureSuccessStatusCode();
        var start = (await startResponse.Content.ReadFromJsonAsync<StartR2Response>())!;

        var commit = await SendAsync(HttpMethod.Post, "/api/uploads/commit-r2", key, new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = FileName,
            ContentType = ContentType,
            FileSize = FileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            ExpiryHours = 24,
        });
        commit.EnsureSuccessStatusCode();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == start.DeliveryId);

        // This is the promise the `write` scope was making all along.
        Assert.Equal(owner.Id, delivery.SenderId);
    }

    [Fact]
    public async Task WriteKey_CannotDelete()
    {
        var (owner, key) = await KeyAsync(ApiScopes.Read + "," + ApiScopes.Write);
        var deliveryId = await SeedDeliveryIdAsync(owner.Id);

        var response = await SendAsync(HttpMethod.Delete, $"/api/deliveries/{deliveryId}", key);

        // Destroying is its own scope, the same split the mounts use.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RevokedKey_StopsWorkingImmediately()
    {
        var (_, key) = await KeyAsync(ApiScopes.Read);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, "/api/deliveries", key)).StatusCode);

        await RevokeAsync(key);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/deliveries", key)).StatusCode);
    }

    [Fact]
    public async Task Key_IsAcceptedInEitherHeaderClientsReachFor()
    {
        var (_, key) = await KeyAsync(ApiScopes.Read);

        using var alternate = new HttpRequestMessage(HttpMethod.Get, "/api/deliveries");
        alternate.Headers.TryAddWithoutValidation(ApiKeyAuthenticator.AlternateHeaderName, key);

        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().SendAsync(alternate)).StatusCode);
    }

    [Fact]
    public async Task Key_SeesOnlyItsOwnAccountsData()
    {
        var (_, key) = await KeyAsync(ApiScopes.Read);
        var (stranger, _) = await KeyAsync(ApiScopes.Read);
        var theirs = await SeedDeliveryAsync(stranger.Id);

        var body = await (await SendAsync(HttpMethod.Get, "/api/deliveries", key)).Content.ReadAsStringAsync();

        // A key is its owner and nobody else — the oldest rule in the product, now reachable
        // through a second credential.
        Assert.DoesNotContain(theirs, body, StringComparison.Ordinal);
    }

    // ── Helpers ──

    private async Task<(User Owner, string Key)> KeyAsync(string scopes)
    {
        var providerKey = Guid.NewGuid().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/developer/keys",
            new CreateApiKeyRequest { Name = "integration", Scopes = scopes });
        response.EnsureSuccessStatusCode();

        return (user, (await response.Content.ReadFromJsonAsync<ApiKeyDto>())!.Key!);
    }

    private async Task RevokeAsync(string rawKey)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var hash = QuickShare.Features.Workspaces.Shared.WorkspaceService.HashToken(rawKey);
        var key = await db.ApiKeys.SingleAsync(k => k.KeyHash == hash);
        key.RevokedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string key, object? body = null)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

        return _factory.CreateClient().SendAsync(request);
    }

    private async Task<string> SeedDeliveryAsync(Guid ownerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shareCode = Guid.NewGuid().ToString("N")[..12];
        db.Deliveries.Add(NewDelivery(ownerId, shareCode));
        await db.SaveChangesAsync();

        return shareCode;
    }

    private async Task<Guid> SeedDeliveryIdAsync(Guid ownerId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var delivery = NewDelivery(ownerId, Guid.NewGuid().ToString("N")[..12]);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();

        return delivery.Id;
    }

    private static Delivery NewDelivery(Guid ownerId, string shareCode) => new()
    {
        Id = Guid.CreateVersion7(),
        SenderId = ownerId,
        FileName = FileName,
        ContentType = ContentType,
        FileSize = FileSize,
        TotalBytes = FileSize,
        ShareCode = shareCode,
        StoragePath = $"test/{Guid.NewGuid():N}",
        Status = DeliveryStatus.Active,
        MaxDownloads = 10,
        CreatedAt = DateTime.UtcNow,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
    };
}
