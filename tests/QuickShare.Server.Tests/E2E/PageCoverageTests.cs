using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>Shell-render coverage for the pages PageLoadTests does not hit.</summary>
public sealed class PageCoverageTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PageCoverageTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/send")]
    [InlineData("/how-to/send-large-files-free")]
    [InlineData("/wetransfer-alternatives")]
    [InlineData("/vi/gui-file-lon-mien-phi")]
    [InlineData("/security")]
    [InlineData("/changelog")]
    [InlineData("/roadmap")]
    [InlineData("/compare")]
    [InlineData("/why-private")]
    [InlineData("/status")]
    [InlineData("/help")]
    public async Task PublicPage_Returns200(string url)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SecurityTxt_ReturnsRfc9116Content()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/.well-known/security.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Contact: mailto:security@quicksharecloud.com", body);
        Assert.Contains("Expires:", body);
        Assert.Contains("Policy:", body);
    }

    [Fact]
    public async Task UploadPage_RendersStripMetadataToggle()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/send");
        Assert.Contains("upload-strip-metadata", html);
    }

    [Fact]
    public async Task Sitemap_ListsPublicPagesOnly_NeverDeliveries()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/sitemap.xml");

        Assert.Contains("<urlset", body);
        Assert.Contains("/pricing", body);
        Assert.DoesNotContain("/d/", body);
    }

    [Fact]
    public async Task Robots_DisallowsAccountAndApi()
    {
        var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/robots.txt");

        Assert.Contains("Disallow: /account", body);
        Assert.Contains("Disallow: /api", body);
        Assert.Contains("Sitemap:", body);
    }

    [Fact]
    public async Task LoginPage_RendersGithubButton()
    {
        var client = _factory.CreateClient();
        var html = await client.GetStringAsync("/login");
        Assert.Contains("login-provider--github", html);
    }

    [Fact]
    public async Task ApiResponse_CarriesRateLimitHeaders()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/deliveries");

        Assert.True(response.Headers.Contains("X-RateLimit-Limit"));
        Assert.True(response.Headers.Contains("X-RateLimit-Remaining"));
        Assert.True(response.Headers.Contains("X-RateLimit-Reset"));
    }

    [Fact]
    public async Task DownloadPage_SeededDelivery_Returns200()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/d/{shareCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task P2PDownloadPage_Returns200()
    {
        var shareCode = await SeedDeliveryAsync();
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/p2p/d/{shareCode}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> SeedDeliveryAsync()
    {
        var shareCode = $"pg-{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "page.bin",
            ContentType = "application/octet-stream",
            FileSize = 10,
            TotalBytes = 10,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 100,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        });
        await db.SaveChangesAsync();
        return shareCode;
    }
}
