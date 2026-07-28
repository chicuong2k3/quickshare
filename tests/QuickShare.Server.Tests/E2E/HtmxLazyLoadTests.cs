using System.Diagnostics;
using System.Net;

namespace QuickShare.Server.Tests.E2E;

public sealed class HtmxLazyLoadTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public HtmxLazyLoadTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UpgradePage_Shell_Returns200_Fast()
    {
        var client = _factory.CreateAuthenticatedClient();
        var sw = Stopwatch.StartNew();
        var response = await client.GetAsync("/upgrade?plan=pro");
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 200,
            $"Shell render took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task UpgradePage_Htmx_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/upgrade?plan=pro");
        request.Headers.Add("HX-Request", "true");

        var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Upgrade", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradePage_ShellToFull_RoundTrip_Under700ms()
    {
        var client = _factory.CreateAuthenticatedClient();

        var sw1 = Stopwatch.StartNew();
        var shellResponse = await client.GetAsync("/upgrade?plan=pro");
        sw1.Stop();
        Assert.Equal(HttpStatusCode.OK, shellResponse.StatusCode);
        Assert.True(sw1.ElapsedMilliseconds < 200);

        var htmxRequest = new HttpRequestMessage(HttpMethod.Get, "/upgrade?plan=pro&billing=");
        htmxRequest.Headers.Add("HX-Request", "true");
        var sw2 = Stopwatch.StartNew();
        var htmxResponse = await client.SendAsync(htmxRequest);
        sw2.Stop();

        Assert.Equal(HttpStatusCode.OK, htmxResponse.StatusCode);
        Assert.True(sw2.ElapsedMilliseconds < 500);

        var total = sw1.ElapsedMilliseconds + sw2.ElapsedMilliseconds;
        Assert.True(total < 700, $"Shell+HTMX total {total}ms");
    }

    [Theory]
    [InlineData("/pricing")]
    [InlineData("/p2p/send")]
    [InlineData("/account")]
    public async Task NonUpgradePage_HtmxRequest_Returns200(string url)
    {
        var client = _factory.CreateAuthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("HX-Request", "true");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
