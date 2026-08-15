using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Inline media preview (issue #21): redirects to the object without counting
/// a download; gated/expired/limit-reached deliveries are never previewed.
/// </summary>
public sealed class DeliveryPreviewTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryPreviewTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Preview_UngatedMedia_Redirects_WithoutCounting()
    {
        var shareCode = await SeedDeliveryAsync(image: true, otpHash: null);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        var response = await client.GetAsync($"/api/downloads/{shareCode}/preview");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);
        Assert.Equal(0, delivery.DownloadCount);
    }

    [Fact]
    public async Task Preview_GatedDelivery_Is403()
    {
        var shareCode = await SeedDeliveryAsync(image: true, otpHash: BCrypt.Net.BCrypt.HashPassword("123456"));

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync($"/api/downloads/{shareCode}/preview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Preview_Expired_Is410()
    {
        var shareCode = await SeedDeliveryAsync(image: true, otpHash: null, expired: true);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync($"/api/downloads/{shareCode}/preview");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
    }

    [Fact]
    public async Task Preview_DownloadLimitReached_Is403()
    {
        var shareCode = await SeedDeliveryAsync(image: true, otpHash: null, downloadCount: 5);

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync($"/api/downloads/{shareCode}/preview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> SeedDeliveryAsync(bool image, string? otpHash, bool expired = false, int downloadCount = 0)
    {
        var shareCode = $"prev{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        db.Deliveries.Add(new QuickShare.Data.Entities.Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = image ? "preview.png" : "preview.bin",
            ContentType = image ? "image/png" : "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            DownloadCount = downloadCount,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = expired ? DateTime.UtcNow.AddHours(-1) : DateTime.UtcNow.AddDays(3),
            OtpHash = otpHash
        });
        await db.SaveChangesAsync();
        return shareCode;
    }
}
