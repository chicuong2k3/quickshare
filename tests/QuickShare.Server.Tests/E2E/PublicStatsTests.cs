using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Downloads.PublicStats;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Public delivery stats (issue #294): opt-in aggregated download counts exposed on
/// /d/{code}/stats and /api/deliveries/{code}/public-stats. Never serves privacy-mode
/// deliveries; aggregates only, no per-recipient identity.
/// </summary>
public sealed class PublicStatsTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PublicStatsTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> SeedAsync(bool publicStats, bool privacyMode = false)
    {
        var shareCode = $"ps{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "stats.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            TotalBytes = 2048,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            PublicStatsEnabled = publicStats,
            PrivacyMode = privacyMode,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return shareCode;
    }

    private async Task SeedTrackingsAsync(string shareCode, params (string Ip, int DaysAgo)[] hits)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var deliveryId = await db.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.Id).FirstAsync();
        foreach (var hit in hits)
        {
            db.DownloadTrackings.Add(new DownloadTracking
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = deliveryId,
                IpAddress = hit.Ip,
                DownloadedAt = DateTime.UtcNow.Date.AddDays(-hit.DaysAgo).AddHours(10),
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Stats_Disabled_Returns404()
    {
        var shareCode = await SeedAsync(publicStats: false);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/deliveries/{shareCode}/public-stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stats_Enabled_ReturnsAggregates()
    {
        var shareCode = await SeedAsync(publicStats: true);
        await SeedTrackingsAsync(shareCode, ("1.1.1.1", 0), ("1.1.1.1", 1), ("2.2.2.2", 2));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/deliveries/{shareCode}/public-stats");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PublicStatsResponse>();
        Assert.NotNull(body);
        Assert.Equal("stats.pdf", body.FileName);
        Assert.Equal(3, body.TotalDownloads);
        Assert.Equal(2, body.UniqueVisitors);
        Assert.Equal(7, body.Timeline.Count);
        Assert.Equal(1, body.Timeline[6].Count);
        Assert.Equal(1, body.Timeline[5].Count);
        Assert.Equal(1, body.Timeline[4].Count);
    }

    [Fact]
    public async Task Stats_PrivacyMode_Returns404()
    {
        var shareCode = await SeedAsync(publicStats: true, privacyMode: true);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/deliveries/{shareCode}/public-stats");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Stats_Page_RendersForEnabled()
    {
        var shareCode = await SeedAsync(publicStats: true);
        await SeedTrackingsAsync(shareCode, ("9.9.9.9", 0));
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/d/{shareCode}/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("stats.pdf", html);
        Assert.Contains("ps-stats", html);
    }

    [Fact]
    public async Task Stats_Page_RendersNotFoundForDisabled()
    {
        var shareCode = await SeedAsync(publicStats: false);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/d/{shareCode}/stats");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("ps-stats", html);
        Assert.Contains("doesn", html);
    }
}
