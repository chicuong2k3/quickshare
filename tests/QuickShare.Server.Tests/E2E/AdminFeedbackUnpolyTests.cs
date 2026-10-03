using System.Net;
using System.Net.Http.Json;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

public sealed class AdminFeedbackUnpolyTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;
    private const string FeedbackRoute = "/admin/feedback";
    private const string FeedbackMessage = "SSR feedback marker";

    public AdminFeedbackUnpolyTests(TestWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DirectAndTargetedGet_ShowAdminFeedbackButNotNonAdmin()
    {
        using var adminClient = _factory.CreateAuthenticatedClient();
        var posted = await adminClient.PostAsJsonAsync("/api/feedback", new
        {
            Sentiment = "up",
            Message = FeedbackMessage,
            Page = "send",
        });
        Assert.Equal(HttpStatusCode.NoContent, posted.StatusCode);

        var direct = await adminClient.GetStringAsync(FeedbackRoute);
        Assert.Contains("id=\"admin-feedback-app\"", direct);
        Assert.Contains(FeedbackMessage, direct);

        using var targetedRequest = new HttpRequestMessage(HttpMethod.Get, FeedbackRoute);
        targetedRequest.Headers.Add(UnpolyHeaders.Target, "#admin-feedback-app");
        var targeted = await adminClient.SendAsync(targetedRequest);
        Assert.Equal(HttpStatusCode.OK, targeted.StatusCode);
        Assert.Contains(FeedbackMessage, await targeted.Content.ReadAsStringAsync());

        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        using var nonAdminClient = _factory.CreateClient();
        nonAdminClient.DefaultRequestHeaders.Add(TestAuthDefaults.UserIdHeader, providerKey);
        nonAdminClient.DefaultRequestHeaders.Add(TestAuthDefaults.EmailHeader, "non-admin@example.test");
        nonAdminClient.DefaultRequestHeaders.Add(TestAuthDefaults.MfaVerifiedHeader, "1");
        var nonAdmin = await nonAdminClient.GetStringAsync(FeedbackRoute);
        Assert.Contains("id=\"admin-feedback-app\"", nonAdmin);
        Assert.DoesNotContain(FeedbackMessage, nonAdmin);
    }
}
