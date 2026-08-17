using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Recipient email gate (issue #324): anonymous downloaders surrender an email (stored on
/// DownloadTracking with a consent flag) before the download is served.
/// </summary>
public sealed class RecipientEmailGateTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public RecipientEmailGateTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> SeedAsync(bool requireEmail)
    {
        var shareCode = $"rg{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "lead.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            RequireRecipientEmail = requireEmail,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    [Fact]
    public async Task Download_WithoutEmail_Returns403()
    {
        var shareCode = await SeedAsync(requireEmail: true);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/downloads/{shareCode}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithEmail_StoresOnTracking()
    {
        var shareCode = await SeedAsync(requireEmail: true);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/downloads/{shareCode}?recipientEmail=lead@example.com&marketingConsent=true");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var tracking = await WaitForTrackingAsync(shareCode);
        Assert.Equal("lead@example.com", tracking.RecipientEmail);
        Assert.True(tracking.MarketingConsent);
    }

    [Fact]
    public async Task Download_NoGate_DoesNotStoreEmail()
    {
        var shareCode = await SeedAsync(requireEmail: false);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        await client.GetAsync($"/api/downloads/{shareCode}");

        var tracking = await WaitForTrackingAsync(shareCode);
        Assert.Null(tracking.RecipientEmail);
        Assert.False(tracking.MarketingConsent);
    }

    private async Task<DownloadTracking> WaitForTrackingAsync(string shareCode)
    {
        for (var i = 0; i < 50; i++)
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deliveryId = await db.Deliveries.Where(d => d.ShareCode == shareCode).Select(d => d.Id).FirstAsync();
            var tracking = await db.DownloadTrackings.FirstOrDefaultAsync(t => t.DeliveryId == deliveryId);
            if (tracking is not null)
                return tracking;
            await Task.Delay(100);
        }

        throw new Xunit.Sdk.XunitException("DownloadTracking row never appeared");
    }
}
