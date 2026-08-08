using System.Net;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Cache strategy is allowlist: only anonymous content-stable pages (Home, Pricing)
/// get public caching; every other page — especially anything personal or on the
/// money path — must be no-store so mutations are never masked by a stale cache.
/// </summary>
public sealed class HtmxCacheTests : IClassFixture<TestWebAppFactory>
{
    private const string CacheControlHeader = "Cache-Control";
    private const string VaryHeader = "Vary";

    private readonly TestWebAppFactory _factory;

    public HtmxCacheTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/")]
    [InlineData("/pricing")]
    public async Task PublicPages_GetPublicCacheHeader(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = string.Join(", ", response.Headers.GetValues(CacheControlHeader));
        Assert.Contains("public", cacheControl);
        Assert.DoesNotContain("no-store", cacheControl);
        Assert.Contains("max-age=300", cacheControl);
        Assert.Contains("HX-Request", string.Join(", ", response.Headers.GetValues(VaryHeader)));
    }

    [Theory]
    [InlineData("/account/profile")]
    [InlineData("/upgrade")]
    public async Task PersonalPages_AreNoStore(string path)
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cacheControl = string.Join(", ", response.Headers.GetValues(CacheControlHeader));
        Assert.Contains("no-store", cacheControl);
        Assert.DoesNotContain("public", cacheControl);
    }
}
