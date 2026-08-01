using System.Diagnostics;
using System.Net;

namespace QuickShare.Server.Tests.E2E;

public sealed class UpgradeFlowTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public UpgradeFlowTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task UpgradePage_Get_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/upgrade?plan=pro");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_StudioPlan_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/upgrade?plan=studio");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_YearlyBilling_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/upgrade?plan=pro&billing=yearly");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_MonthlyBilling_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/upgrade?plan=pro&billing=monthly");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_PostForm_Returns200()
    {
        var client = _factory.CreateAuthenticatedClient();
        var getResponse = await client.GetAsync("/upgrade?plan=pro");
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        var tokenStart = getHtml.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        var valueStart = getHtml.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = getHtml.IndexOf("\"", valueStart, StringComparison.Ordinal);
        var token = getHtml.Substring(valueStart, valueEnd - valueStart);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Plan", "pro"),
            new KeyValuePair<string, string>("Billing", "monthly"),
        });
        var response = await client.PostAsync("/upgrade?plan=pro", formContent);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_PostForm_Returns200_WithToken()
    {
        var client = _factory.CreateAuthenticatedClient();
        var getResponse = await client.GetAsync("/upgrade?plan=pro");
        var getHtml = await getResponse.Content.ReadAsStringAsync();

        var tokenStart = getHtml.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        Assert.True(tokenStart >= 0, "Antiforgery token input not found in GET response");
        var valueStart = getHtml.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = getHtml.IndexOf("\"", valueStart, StringComparison.Ordinal);
        var token = getHtml.Substring(valueStart, valueEnd - valueStart);
        Assert.NotEmpty(token);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Plan", "pro"),
            new KeyValuePair<string, string>("Billing", "yearly"),
        });
        var postResponse = await client.PostAsync("/upgrade?plan=pro", formContent);
        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);
    }

    [Fact]
    public async Task UpgradePage_PostResponse_Under500ms()
    {
        var client = _factory.CreateAuthenticatedClient();
        var getResponse = await client.GetAsync("/upgrade?plan=pro");
        var getHtml = await getResponse.Content.ReadAsStringAsync();
        var tokenStart = getHtml.IndexOf("__RequestVerificationToken", StringComparison.Ordinal);
        var valueStart = getHtml.IndexOf("value=\"", tokenStart, StringComparison.Ordinal) + 7;
        var valueEnd = getHtml.IndexOf("\"", valueStart, StringComparison.Ordinal);
        var token = getHtml.Substring(valueStart, valueEnd - valueStart);

        var formContent = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("__RequestVerificationToken", token),
            new KeyValuePair<string, string>("Plan", "pro"),
            new KeyValuePair<string, string>("Billing", "monthly"),
        });
        var sw = Stopwatch.StartNew();
        var response = await client.PostAsync("/upgrade?plan=pro", formContent);
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(sw.ElapsedMilliseconds < 500,
            $"POST /upgrade took {sw.ElapsedMilliseconds}ms");
    }

}

