using System.Net;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class UnpolyMigrationTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    private const string UnpolyScriptPath = "_content/Unpoly.Blazor/unpoly.min.js";

    public UnpolyMigrationTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task PublicPage_ExposesUnpolyAndVariesByTarget()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(RoutePaths.Home);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(UnpolyScriptPath, html);
        Assert.Contains(UnpolyHeaders.Target, string.Join(", ", response.Headers.Vary));
    }

    [Fact]
    public async Task UnpolyScript_IsServedAsJavascript()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/{UnpolyScriptPath}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }
}
