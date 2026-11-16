using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Delivery scope control end to end (issue #142 CIDR allowlist, issue #166 country
/// allow/deny). Exercises the real request pipeline: the caller address arrives through
/// X-Forwarded-For (Program.cs clears KnownProxies/KnownNetworks, so the header is
/// trusted and rewrites RemoteIpAddress) and the country through CF-IPCountry, the same
/// edge header product analytics reads.
/// </summary>
public sealed class DeliveryAccessScopeTests : IClassFixture<TestWebAppFactory>
{
    private const string OfficeRange = "203.0.113.0/24";
    private const string InsideOffice = "203.0.113.7";
    private const string OutsideOffice = "198.51.100.7";

    private readonly TestWebAppFactory _factory;

    public DeliveryAccessScopeTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> SeedAsync(
        string? allowedIpRanges = null,
        string? allowedCountries = null,
        string? blockedCountries = null)
    {
        var shareCode = $"ac{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "contract.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            AllowedIpRanges = allowedIpRanges,
            AllowedCountries = allowedCountries,
            BlockedCountries = blockedCountries,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private HttpClient Client() => _factory.CreateClient(
        new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string? ip, string? country)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (ip is not null)
            request.Headers.Add("X-Forwarded-For", ip);
        if (country is not null)
            request.Headers.Add("CF-IPCountry", country);

        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Download_FromAllowedRange_IsServed()
    {
        var shareCode = await SeedAsync(allowedIpRanges: OfficeRange);

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_FromOutsideRange_Is403()
    {
        var shareCode = await SeedAsync(allowedIpRanges: OfficeRange);

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", OutsideOffice, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_FromAllowedCountry_IsServed()
    {
        var shareCode = await SeedAsync(allowedCountries: "VN");

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, "VN");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Download_FromDisallowedCountry_Is403()
    {
        var shareCode = await SeedAsync(allowedCountries: "VN");

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, "US");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_FromBlockedCountry_Is403()
    {
        var shareCode = await SeedAsync(blockedCountries: "RU");

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, "RU");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_WithCountryRuleAndNoEdgeHeader_Is403()
    {
        var shareCode = await SeedAsync(allowedCountries: "VN");

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_RulesAreAndScoped()
    {
        var shareCode = await SeedAsync(allowedIpRanges: OfficeRange, allowedCountries: "VN");

        var allowed = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, "VN");
        Assert.Equal(HttpStatusCode.Redirect, allowed.StatusCode);

        var wrongIp = await GetAsync(Client(), $"/api/downloads/{shareCode}", OutsideOffice, "VN");
        Assert.Equal(HttpStatusCode.Forbidden, wrongIp.StatusCode);

        var wrongCountry = await GetAsync(Client(), $"/api/downloads/{shareCode}", InsideOffice, "US");
        Assert.Equal(HttpStatusCode.Forbidden, wrongCountry.StatusCode);
    }

    [Fact]
    public async Task Download_WithoutRules_IsServedFromAnywhere()
    {
        var shareCode = await SeedAsync();

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}", OutsideOffice, "RU");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task ZipContents_HonoursTheSameRules()
    {
        var shareCode = await SeedAsync(allowedIpRanges: OfficeRange);

        var response = await GetAsync(Client(), $"/api/downloads/{shareCode}/zip-contents", OutsideOffice, null);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPage_ForRefusedCaller_DoesNotLeakTheFileName()
    {
        var shareCode = await SeedAsync(allowedIpRanges: OfficeRange);

        var response = await GetAsync(Client(), $"/d/{shareCode}", OutsideOffice, null);
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("contract.pdf", html, StringComparison.OrdinalIgnoreCase);
    }
}
