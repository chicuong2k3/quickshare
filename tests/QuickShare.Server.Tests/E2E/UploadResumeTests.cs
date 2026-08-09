using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Resume over time (issue #3): part URLs stay obtainable long after the 15-min
/// presigned TTL (sessions are backdated instead of fake-clocked), and the
/// zombie sweeper only reaps sessions past their expiry.
/// </summary>
public sealed class UploadResumeTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public UploadResumeTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<StartR2Response> StartUploadAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = "resume-me.bin",
            FileSize = 1024,
            ContentType = "application/octet-stream",
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var start = await response.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);
        return start;
    }

    private async Task BackdateSessionAsync(Guid sessionId, TimeSpan createdAgo, DateTime? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var createdAt = DateTime.UtcNow - createdAgo;
        await db.UploadSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(s => s.CreatedAt, createdAt)
                .SetProperty(s => s.ExpiresAt, expiresAt ?? DateTime.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task PartUrls_SessionOlderThan15Minutes_StillReturnsFreshUrls()
    {
        var client = _factory.CreateAuthenticatedClient();
        var start = await StartUploadAsync(client);

        // Simulate the user coming back to the error screen 20 minutes later,
        // long after the original presigned part URLs (15-min TTL) died.
        await BackdateSessionAsync(start.DeliveryId, TimeSpan.FromMinutes(20));

        var response = await client.PostAsJsonAsync("/api/uploads/part-urls", new
        {
            start.DeliveryId,
            start.UploadId,
            PartNumbers = new[] { 1 },
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("stub.local", body);
    }

    [Fact]
    public async Task ZombieSweep_ReapsSessionPastExpiry_KeepsLiveSession()
    {
        var client = _factory.CreateAuthenticatedClient();
        var live = await StartUploadAsync(client);
        var zombie = await StartUploadAsync(client);
        await BackdateSessionAsync(zombie.DeliveryId, TimeSpan.FromHours(3), expiresAt: DateTime.UtcNow.AddMinutes(-1));

        var sweeper = new ZombiePartSweeper(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<IStorageProvider>(),
            NullLogger<ZombiePartSweeper>.Instance);
        await sweeper.SweepZombiePartsAsync(CancellationToken.None);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.UploadSessions.FindAsync(zombie.DeliveryId));
        Assert.NotNull(await db.UploadSessions.FindAsync(live.DeliveryId));
    }
}
