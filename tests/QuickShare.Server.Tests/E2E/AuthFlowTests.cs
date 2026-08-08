using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QuickShare.Server.Tests.E2E;

public sealed class AuthFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AuthFlowTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/deliveries")]
    [InlineData("/api/me/avatar")]
    public async Task ApiEndpoint_Unauthenticated_Returns401(string url)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_RedirectsHome()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/logout");
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/?logout=1", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task LoginPage_ShowsGoogleSignIn()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/login");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("google", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginChallenge_UnknownProvider_Returns404()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/login/facebook");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
