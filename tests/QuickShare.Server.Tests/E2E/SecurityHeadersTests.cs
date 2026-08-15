using System.Net;
using Microsoft.Extensions.DependencyInjection;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Security headers (issue #82) and delivery-page robots tags (issue #121).
/// </summary>
public sealed class SecurityHeadersTests : IClassFixture<TestWebAppFactory>
{
    private const string XRobotsTagHeader = "X-Robots-Tag";
    private const string NoIndexNoFollow = "noindex, nofollow";

    private readonly TestWebAppFactory _factory;

    public SecurityHeadersTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    [InlineData("/login")]
    public async Task EveryResponse_CarriesSecurityHeaders(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
        Assert.True(response.Headers.Contains("Permissions-Policy"));
        var csp = string.Join(" ", response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.Contains("object-src 'none'", csp);
    }

    [Fact]
    public async Task DeliveryPage_HasNoIndexHeader()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.GetAsync($"/d/{shareCode}");
        var allHeaders = string.Join("; ", response.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(XRobotsTagHeader), $"Missing {XRobotsTagHeader}. Headers: {allHeaders}");
        Assert.Equal(NoIndexNoFollow, response.Headers.GetValues(XRobotsTagHeader).First());
    }

    [Fact]
    public async Task DeliveryPage_HasRobotsMetaTag()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
    }

    [Fact]
    public async Task MarketingPages_AreStillIndexable()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/pricing");

        Assert.False(response.Headers.Contains(XRobotsTagHeader));
    }

    [Fact]
    public async Task DownloadApiResponse_HasNoIndexHeader()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        var response = await client.GetAsync($"/api/downloads/{shareCode}");

        Assert.Equal(NoIndexNoFollow, response.Headers.GetValues(XRobotsTagHeader).First());
    }

    private async Task<string> SeedDeliveryAsync()
    {
        var shareCode = $"sec{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        db.Deliveries.Add(new QuickShare.Data.Entities.Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "security-test.bin",
            ContentType = "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3)
        });
        await db.SaveChangesAsync();
        return shareCode;
    }
}
