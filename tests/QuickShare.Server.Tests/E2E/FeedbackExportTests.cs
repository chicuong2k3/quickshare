using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Tier-5 feedback (issue #35) + delivery CSV export (issue #74).
/// </summary>
public sealed class FeedbackExportTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public FeedbackExportTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FeedbackSubmit_StoresAnonymousFeedback()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/feedback", new
        {
            sentiment = "up",
            message = "great",
            page = "send"
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var feedback = await db.Feedbacks.AsNoTracking().FirstOrDefaultAsync();
        Assert.NotNull(feedback);
        Assert.Equal("up", feedback!.Sentiment);
        Assert.Equal("send", feedback.Page);
    }

    [Fact]
    public async Task ExportCsv_FreeUser_Is403()
    {
        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/deliveries/export");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ExportCsv_ProUser_ReturnsHeader()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.GetAsync("/api/deliveries/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("ShareCode,FileName,DownloadedAt,IpHash", body);
    }
}
