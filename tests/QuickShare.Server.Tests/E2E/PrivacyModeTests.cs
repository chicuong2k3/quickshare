using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Per-delivery privacy mode (issue #187): zero download tracking, zero analytics, and
/// the visible count is hidden — while expiry/password/burn gates keep working.
/// </summary>
public sealed class PrivacyModeTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PrivacyModeTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> SeedAsync(Action<Delivery> mutate)
    {
        var shareCode = $"priv{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "private.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
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

    [Fact]
    public async Task DownloadPage_HidesCount_AndShowsPrivacyBadge()
    {
        var shareCode = await SeedAsync(d =>
        {
            d.PrivacyMode = true;
            d.DownloadCount = 3;
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.DoesNotContain("Downloaded 3 times", html);
        Assert.Contains("Privacy mode", html);
    }

    [Fact]
    public async Task Download_IncrementsCount_ButWritesNoTracking()
    {
        var shareCode = await SeedAsync(d => { d.PrivacyMode = true; d.DownloadCount = 0; });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        var response = await client.GetAsync($"/api/downloads/{shareCode}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var delivery = await WaitForCountAsync(shareCode, expected: 1);
        Assert.Equal(1, delivery.DownloadCount);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackingCount = await db.DownloadTrackings.CountAsync(t => t.DeliveryId == delivery.Id);
        Assert.Equal(0, trackingCount);
    }

    [Fact]
    public async Task NonPrivacyDownload_WritesTracking()
    {
        var shareCode = await SeedAsync(d => d.PrivacyMode = false);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });
        await client.GetAsync($"/api/downloads/{shareCode}");

        var delivery = await WaitForCountAsync(shareCode, expected: 1);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var trackingCount = await db.DownloadTrackings.CountAsync(t => t.DeliveryId == delivery.Id);
        Assert.Equal(1, trackingCount);
    }

    private async Task<Delivery> WaitForCountAsync(string shareCode, int expected)
    {
        for (var i = 0; i < 50; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
            if (delivery.DownloadCount >= expected)
                return delivery;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException($"DownloadCount never reached {expected}");
    }
}
