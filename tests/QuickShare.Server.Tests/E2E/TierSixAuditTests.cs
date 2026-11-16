using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Developer;
using QuickShare.Features.Workspaces.Domains;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Regressions found auditing batches 19–24 (white-label, mounts, gateway, voice, crypto, OCR).
///
/// Each of these was a rule that held on the path it was written for and nowhere else: the
/// tenant-host check guarded the download page while the stats page sat one route away, and
/// suspension held on the recipient paths while a mount served the same file happily. The shape
/// of the mistake is always the same — a gate enforced at a call site instead of at the thing
/// every call site goes through.
/// </summary>
public sealed class TierSixAuditTests : IClassFixture<TestWebAppFactory>
{
    /// <summary>One host per test: a verified hostname belongs to exactly one workspace, which
    /// is the invariant under test — sharing a constant across tests would collide on it.</summary>
    private static string NewTenantHost() => $"files.{Guid.NewGuid():N}.test"[..24] + ".test";
    private const string Dav = "/dav";

    private readonly TestWebAppFactory _factory;

    public TierSixAuditTests(TestWebAppFactory factory) => _factory = factory;

    // ── #81: the tenant host is scoped to that workspace, on every recipient surface ──

    [Fact]
    public async Task TenantHost_DoesNotServeAnotherWorkspacesStatsPage()
    {
        var tenantHost = NewTenantHost();
        var (client, workspaceId) = await WorkspaceAsync();
        await VerifiedDomainAsync(client, tenantHost);

        var mine = await SeedDeliveryAsync(workspaceId, publicStats: true);
        var theirs = await SeedDeliveryAsync((await WorkspaceAsync()).WorkspaceId, publicStats: true);

        var anonymous = _factory.CreateClient();

        var ours = await GetAsync(anonymous, $"/d/{mine}/stats", tenantHost);
        Assert.Contains("stats", ours, StringComparison.OrdinalIgnoreCase);

        // Somebody else's download numbers under the agency's own domain is the same failure as
        // somebody else's file — the guard belonged on the reader, not on one page.
        var foreign = await GetAsync(anonymous, $"/d/{theirs}/stats", tenantHost);
        Assert.DoesNotContain(theirs, foreign, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantHost_SendsAppPagesBackToTheCanonicalAddress()
    {
        var tenantHost = NewTenantHost();
        var (client, _) = await WorkspaceAsync();
        await VerifiedDomainAsync(client, tenantHost);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/login");
        request.Headers.Host = tenantHost;

        using var handlerClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var response = await handlerClient.SendAsync(request);

        // A sign-in page on a hostname we do not control is an invitation to type our
        // credentials somewhere else. Redirected, not 404: the visitor still wanted the product.
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(tenantHost, response.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TenantHost_StillServesTheRecipientPageAndItsApi()
    {
        var tenantHost = NewTenantHost();
        var (client, workspaceId) = await WorkspaceAsync();
        await VerifiedDomainAsync(client, tenantHost);
        var shareCode = await SeedDeliveryAsync(workspaceId);

        var anonymous = _factory.CreateClient();

        // The page is built out of these; blocking them would serve a broken page rather than a
        // scoped one.
        Assert.Contains("audit.pdf", await GetAsync(anonymous, $"/d/{shareCode}", tenantHost), StringComparison.Ordinal);

        // Not following the redirect on purpose: the download endpoint answers with the storage
        // location, and an explicit Host header would ride along to whatever it points at.
        using var direct = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });

        using var api = new HttpRequestMessage(HttpMethod.Get, $"/api/downloads/{shareCode}");
        api.Headers.Host = tenantHost;
        var apiResponse = await direct.SendAsync(api);

        // Served, not scoped away: the download endpoint reached storage rather than being sent
        // back to the canonical host.
        Assert.Equal(HttpStatusCode.Found, apiResponse.StatusCode);
        Assert.Contains("stub.local", apiResponse.Headers.Location!.ToString(), StringComparison.Ordinal);
    }

    // ── #53 suspension holds on the new surfaces ──

    [Fact]
    public async Task SuspendedDelivery_IsNotVisibleThroughAMount()
    {
        var (client, workspaceId) = await WorkspaceAsync();
        var key = await ApiKeyAsync(client, ApiScopes.Read);
        var shareCode = await SeedDeliveryAsync(workspaceId);

        var listed = await PropfindAsync(key);
        Assert.Contains("audit.pdf", listed, StringComparison.Ordinal);

        await SuspendAsync(shareCode);

        // Suspension is applied *to* the owner, so "it is their own file" is exactly the
        // argument that does not hold. A moderation decision has to hold on every path.
        var afterwards = await PropfindAsync(key);
        Assert.DoesNotContain("audit.pdf", afterwards, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuspendedDelivery_IsNotIndexedForSearch()
    {
        var (client, workspaceId) = await WorkspaceAsync();
        var shareCode = await SeedDeliveryAsync(workspaceId, fileName: "flyer.png", contentType: "image/png", ocr: true);
        await SuspendAsync(shareCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<QuickShare.Features.Deliveries.Ocr.OcrJob>().RunAsync();
        }

        await using var verify = _factory.Services.CreateAsyncScope();
        var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();

        // Indexing a suspended share would put its content back in front of somebody by search.
        Assert.False(await db.DeliveryOcrTexts.AsNoTracking()
            .AnyAsync(o => db.Deliveries.Any(d => d.Id == o.DeliveryId && d.ShareCode == shareCode)));
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var created = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Audit {Guid.NewGuid():N}"[..16], Seats = 3 });
        created.EnsureSuccessStatusCode();

        return (client, (await created.Content.ReadFromJsonAsync<WorkspaceDto>())!.Id);
    }

    private static async Task VerifiedDomainAsync(HttpClient client, string host)
    {
        (await client.PutAsJsonAsync("/api/workspace/domain", new DomainRequest { Host = host })).EnsureSuccessStatusCode();

        lock (StubDomainVerifier.Pointed)
        {
            StubDomainVerifier.Pointed.Add(host);
        }

        var verified = (await (await client.PostAsync("/api/workspace/domain/verify", null))
            .Content.ReadFromJsonAsync<DomainDto>())!;
        Assert.True(verified.Verified);
    }

    private static async Task<string> ApiKeyAsync(HttpClient client, string scopes)
    {
        var response = await client.PostAsJsonAsync("/api/developer/keys",
            new CreateApiKeyRequest { Name = "audit", Scopes = scopes });
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<ApiKeyDto>())!.Key!;
    }

    private async Task<string> SeedDeliveryAsync(
        Guid workspaceId, string fileName = "audit.pdf", string contentType = "application/pdf",
        bool publicStats = false, bool ocr = false)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var shareCode = Guid.NewGuid().ToString("N")[..12];
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            FileName = fileName,
            ContentType = contentType,
            FileSize = 2048,
            TotalBytes = 2048,
            ShareCode = shareCode,
            StoragePath = $"test/{Guid.NewGuid():N}",
            Status = DeliveryStatus.Active,
            MaxDownloads = 10,
            PublicStatsEnabled = publicStats,
            OcrRequested = ocr,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();

        return shareCode;
    }

    private async Task SuspendAsync(string shareCode)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
        delivery.Status = DeliveryStatus.Suspended;
        await db.SaveChangesAsync();
    }

    private async Task<string> PropfindAsync(string key)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), Dav);
        request.Headers.Add("Depth", "1");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"quickshare:{key}")));

        var response = await _factory.CreateClient().SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetAsync(HttpClient client, string url, string host)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Host = host;

        var response = await client.SendAsync(request);

        return await response.Content.ReadAsStringAsync();
    }
}
