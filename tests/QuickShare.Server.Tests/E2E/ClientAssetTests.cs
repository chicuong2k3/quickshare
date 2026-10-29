using System.Net;
using System.Text.RegularExpressions;

namespace QuickShare.Server.Tests.E2E;

public sealed class ClientAssetTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public ClientAssetTests(TestWebAppFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/QuickShare.styles.css")]
    [InlineData("/css/shadcn.generated.css")]
    [InlineData("/js/account.js")]
    [InlineData("/_content/Unpoly.Blazor.Shadcn/ui.js")]
    public async Task ClientAsset_IsServed(string path)
    {
        using var client = _factory.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ScopedBundle_IncludesDeliveryStyles()
    {
        using var client = _factory.CreateClient();
        var css = await client.GetStringAsync("/QuickShare.styles.css");

        Assert.Contains("account-delivery__gauge", css);
    }

    [Fact]
    public async Task ServiceWorkerShellAssets_AreServed()
    {
        using var client = _factory.CreateClient();
        var script = await client.GetStringAsync("/sw.js");
        var shell = script.Split("const SHELL = [", 2)[1].Split("];", 2)[0];
        var paths = Regex.Matches(shell, "'(/[^']+)'");
        Assert.NotEmpty(paths);

        foreach (Match match in paths)
        {
            using var response = await client.GetAsync(match.Groups[1].Value);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
