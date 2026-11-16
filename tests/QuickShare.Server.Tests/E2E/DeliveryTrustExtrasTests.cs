using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Tier-4 trust/retention batch: burn-after-download (#12), SHA-256 checksum (#66),
/// link extend (#45), public download stats (#64).
/// </summary>
public sealed class DeliveryTrustExtrasTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "trust-test.bin";
    private const string TestContentType = "application/octet-stream";
    private const long TestFileSize = 1024;
    private const string ValidSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private readonly TestWebAppFactory _factory;

    public DeliveryTrustExtrasTests(TestWebAppFactory factory) => _factory = factory;

    // ── #66 SHA-256 ──

    [Fact]
    public async Task Upload_StoresSha256_AndInvalidIsRejected()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var shareCode = await UploadAsync(client, sha256: ValidSha256);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        Assert.Equal(ValidSha256, delivery.Sha256);

        var bad = await UploadRawAsync(client, sha256: "not-hex");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task DownloadPage_RendersSha256()
    {
        var shareCode = await SeedDeliveryAsync(d => d.Sha256 = ValidSha256);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.Contains(ValidSha256, html);
    }

    // ── #12 burn-after-download ──

    [Fact]
    public async Task Upload_BurnAfterDownload_SetsMaxDownloadsToOne()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var shareCode = await UploadAsync(client, burnAfterDownload: true);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        Assert.Equal(1, delivery.MaxDownloads);
    }

    [Fact]
    public async Task DownloadPage_BurnDelivery_RendersBadgeAndWarning()
    {
        var shareCode = await SeedDeliveryAsync(d => d.MaxDownloads = 1);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.Contains("Single-use", html);
        Assert.Contains("only be opened once", html);
    }

    [Fact]
    public async Task PageView_DoesNotIncrementDownloadCount()
    {
        var shareCode = await SeedDeliveryAsync(d => { d.MaxDownloads = 1; d.DownloadCount = 0; });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await client.GetStringAsync($"/d/{shareCode}");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        Assert.Equal(0, delivery.DownloadCount);
    }

    // ── #45 extend ──

    [Fact]
    public async Task Extend_NearExpiry_ExtendsBySevenDays()
    {
        var shareCode = await SeedDeliveryAsync(d => d.ExpiresAt = DateTime.UtcNow.AddHours(1));
        var id = await DeliveryIdAsync(shareCode);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsync($"/api/deliveries/{id}/extend", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        Assert.True(delivery.ExpiresAt > DateTime.UtcNow.AddDays(6));
    }

    [Fact]
    public async Task Extend_FreePlan_TooEarly_Is400()
    {
        var shareCode = await SeedDeliveryAsync(d => d.ExpiresAt = DateTime.UtcNow.AddHours(48));
        var id = await DeliveryIdAsync(shareCode);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsync($"/api/deliveries/{id}/extend", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── #64 public download stats ──

    [Fact]
    public async Task DownloadPage_ShowsDownloadCount_WhenNotHidden()
    {
        var shareCode = await SeedDeliveryAsync(d =>
        {
            d.DownloadCount = 3;
            d.FirstDownloadedAt = DateTime.UtcNow.AddHours(-2);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.Contains("Downloaded 3 times", html);
    }

    [Fact]
    public async Task DownloadPage_HidesStats_WhenSenderHides()
    {
        var shareCode = await SeedDeliveryAsync(d =>
        {
            d.DownloadCount = 3;
            d.HideDownloadStats = true;
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("Downloaded 3 times", html);
    }

    // ── helpers ──

    private static async Task<string> UploadAsync(HttpClient client, string? sha256 = null, bool burnAfterDownload = false)
    {
        var commit = await UploadRawAsync(client, sha256, burnAfterDownload);
        Assert.Equal(HttpStatusCode.Created, commit.StatusCode);
        var body = await commit.Content.ReadFromJsonAsync<CommitR2Response>();
        return body!.ShareCode;
    }

    private static async Task<HttpResponseMessage> UploadRawAsync(HttpClient client, string? sha256 = null, bool burnAfterDownload = false)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();

        return await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start!.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            Sha256 = sha256,
            BurnAfterDownload = burnAfterDownload,
        });
    }

    private async Task<string> SeedDeliveryAsync(Action<Delivery> mutate)
    {
        var shareCode = $"trust{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            TotalBytes = TestFileSize,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
        };
        mutate(delivery);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return shareCode;
    }

    private async Task<Guid> DeliveryIdAsync(string shareCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        return delivery.Id;
    }
}
