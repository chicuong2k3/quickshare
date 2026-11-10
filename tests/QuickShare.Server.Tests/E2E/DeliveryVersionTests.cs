using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Replace;
using QuickShare.Features.Deliveries.Versions;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// File version history (#110): Pro/Studio replace preserves previous versions,
/// sender lists version history, recipient downloads past versions, and compliance/gating
/// invariants apply across version endpoints and retention caps.
/// </summary>
public sealed class DeliveryVersionTests : IClassFixture<TestWebAppFactory>
{
    private const string DeliveriesApi = "/api/deliveries";

    private readonly TestWebAppFactory _factory;

    public DeliveryVersionTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<Delivery> SeedDeliveryAsync(
        Guid userId,
        string fileName = "v1.pdf",
        long fileSize = 1024,
        string storagePath = "stub/original-v1",
        string? sha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        string status = DeliveryStatus.Active,
        DateTime? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            FileName = fileName,
            ContentType = "application/pdf",
            FileSize = fileSize,
            TotalBytes = fileSize,
            ShareCode = Guid.NewGuid().ToString("N")[..10],
            StoragePath = storagePath,
            Sha256 = sha256,
            MaxDownloads = 5,
            Status = status,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(2),
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    private static async Task ReplaceFileAsync(
        HttpClient client,
        Guid deliveryId,
        string newFileName,
        long newFileSize,
        string? newSha256 = null)
    {
        var startRes = await client.PostAsJsonAsync($"{DeliveriesApi}/{deliveryId}/replace/start", new
        {
            FileName = newFileName,
            FileSize = newFileSize,
            ContentType = "application/pdf",
            PartNumbers = new[] { 1 }
        });
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
        var start = await startRes.Content.ReadFromJsonAsync<ReplaceDeliveryStartResponse>();
        Assert.NotNull(start);

        var commitRes = await client.PostAsJsonAsync($"{DeliveriesApi}/{deliveryId}/replace/commit", new
        {
            SessionId = start.SessionId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            Parts = new[] { new R2PartEtag(1, "etag-1") },
            Sha256 = newSha256
        });
        Assert.Equal(HttpStatusCode.NoContent, commitRes.StatusCode);
    }

    [Fact]
    public async Task FreeUser_Replace_DoesNotCreateVersionSnapshot()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Free);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "free-v1.pdf");
        var client = _factory.CreateAuthenticatedClient(key);

        await ReplaceFileAsync(client, delivery.Id, "free-v2.pdf", 2048);

        var versions = await client.GetFromJsonAsync<List<VersionDto>>($"{DeliveriesApi}/{delivery.Id}/versions");
        Assert.NotNull(versions);
        Assert.Empty(versions);
    }

    [Fact]
    public async Task ProUser_Replace_SnapshotsPreviousVersions_NewestFirst()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        const string sha1 = "1111111111111111111111111111111111111111111111111111111111111111";
        const string sha2 = "2222222222222222222222222222222222222222222222222222222222222222";
        const string sha3 = "3333333333333333333333333333333333333333333333333333333333333333";

        var delivery = await SeedDeliveryAsync(user.Id, fileName: "contract-v1.pdf", fileSize: 1000, sha256: sha1);
        var client = _factory.CreateAuthenticatedClient(key);

        await ReplaceFileAsync(client, delivery.Id, "contract-v2.pdf", 2000, sha2);
        await ReplaceFileAsync(client, delivery.Id, "contract-v3.pdf", 3000, sha3);

        var versions = await client.GetFromJsonAsync<List<VersionDto>>($"{DeliveriesApi}/{delivery.Id}/versions");
        Assert.NotNull(versions);
        Assert.Equal(2, versions.Count);

        // Newest snapshot first (v2, then v1).
        Assert.Equal(2, versions[0].VersionNumber);
        Assert.Equal("contract-v2.pdf", versions[0].FileName);
        Assert.Equal(2000, versions[0].FileSize);
        Assert.Equal(sha2, versions[0].Sha256);

        Assert.Equal(1, versions[1].VersionNumber);
        Assert.Equal("contract-v1.pdf", versions[1].FileName);
        Assert.Equal(1000, versions[1].FileSize);
        Assert.Equal(sha1, versions[1].Sha256);
    }

    [Fact]
    public async Task DownloadPage_RendersPreviousVersionsList_WhenSnapshotsExist()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "deck-v1.pdf", fileSize: 1024);
        var client = _factory.CreateAuthenticatedClient(key);

        await ReplaceFileAsync(client, delivery.Id, "deck-v2.pdf", 2048);

        var anon = _factory.CreateClient();
        var pageRes = await anon.GetAsync($"/d/{delivery.ShareCode}");
        Assert.Equal(HttpStatusCode.OK, pageRes.StatusCode);

        var html = await pageRes.Content.ReadAsStringAsync();
        Assert.Contains("id=\"dl-versions\"", html);
        Assert.Contains($"/api/downloads/{delivery.ShareCode}/versions/1", html);
        Assert.Contains("deck-v1.pdf", html);
    }

    [Fact]
    public async Task DownloadVersion_RedirectsToPresignedUrl_AndRespectsLifecycleGates()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "spec-v1.pdf", storagePath: "stub/spec-v1-key");
        var client = _factory.CreateAuthenticatedClient(key);

        await ReplaceFileAsync(client, delivery.Id, "spec-v2.pdf", 2048);

        var noRedirectClient = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Valid version 1 redirects (302) to presigned URL.
        var v1Res = await noRedirectClient.GetAsync($"/api/downloads/{delivery.ShareCode}/versions/1");
        Assert.Equal(HttpStatusCode.Redirect, v1Res.StatusCode);
        Assert.NotNull(v1Res.Headers.Location);

        // Nonexistent version returns 404.
        var v99Res = await noRedirectClient.GetAsync($"/api/downloads/{delivery.ShareCode}/versions/99");
        Assert.Equal(HttpStatusCode.NotFound, v99Res.StatusCode);

        // PendingApproval (#162) returns 404 (indistinguishable from not-found).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
            row.Status = DeliveryStatus.PendingApproval;
            await db.SaveChangesAsync();
        }
        var pendingRes = await noRedirectClient.GetAsync($"/api/downloads/{delivery.ShareCode}/versions/1");
        Assert.Equal(HttpStatusCode.NotFound, pendingRes.StatusCode);

        // Expired delivery returns 410 Gone.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
            row.Status = DeliveryStatus.Active;
            row.ExpiresAt = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
        }
        var expiredRes = await noRedirectClient.GetAsync($"/api/downloads/{delivery.ShareCode}/versions/1");
        Assert.Equal(HttpStatusCode.Gone, expiredRes.StatusCode);
    }

    [Fact]
    public async Task ProUser_AtMaxVersionsCap_DropsOldestVersionOnReplace()
    {
        var key = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(key, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "current.pdf", storagePath: "stub/current");
        var client = _factory.CreateAuthenticatedClient(key);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 1; i <= DeliveryVersion.MaxVersionsPro; i++)
            {
                db.DeliveryVersions.Add(new DeliveryVersion
                {
                    Id = Guid.CreateVersion7(),
                    DeliveryId = delivery.Id,
                    VersionNumber = i,
                    FileName = $"v{i}.pdf",
                    ContentType = "application/pdf",
                    FileSize = 100 * i,
                    StoragePath = $"stub/v{i}",
                });
            }
            await db.SaveChangesAsync();
        }

        await ReplaceFileAsync(client, delivery.Id, "next.pdf", 4096);

        var versions = await client.GetFromJsonAsync<List<VersionDto>>($"{DeliveriesApi}/{delivery.Id}/versions");
        Assert.NotNull(versions);
        Assert.Equal(DeliveryVersion.MaxVersionsPro, versions.Count);
        Assert.DoesNotContain(versions, v => v.VersionNumber == 1);
        Assert.Contains(versions, v => v.VersionNumber == DeliveryVersion.MaxVersionsPro + 1);
    }
}
