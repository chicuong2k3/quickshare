using System.Net;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class PageRenderingTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public PageRenderingTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/pricing")]
    [InlineData("/p2p/send")]
    [InlineData("/account")]
    [InlineData("/upgrade?plan=pro")]
    public async Task DirectGet_RendersUsableDocument(string url)
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var response = await client.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<main", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnpolyTarget_ReturnsRequestedAccountContent()
    {
        using var client = _factory.CreateAuthenticatedClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, RoutePaths.Account);
        request.Headers.Add(UnpolyHeaders.Target, "#account-app");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"account-app\"", await response.Content.ReadAsStringAsync());
    }
}
