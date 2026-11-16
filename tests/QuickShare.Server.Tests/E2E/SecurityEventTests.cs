using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Account security audit trail (issue #126): append-only events record sign-ins
/// and revokes, raw IPs never hit the database, retention purges old rows, and
/// the GDPR export carries the trail.
/// </summary>
public sealed class SecurityEventTests : IClassFixture<TestWebAppFactory>
{
    private const string TestUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";
    private const string TestIp = "203.0.113.7";

    private readonly TestWebAppFactory _factory;

    public SecurityEventTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task AppendAsync_StoresHashedIpAndSummarizedAgent()
    {
        var userId = _factory.TestUserId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.RemoveRange(db.SecurityEvents.Where(e => e.UserId == userId));
            await db.SaveChangesAsync();
            await SecurityEventService.AppendAsync(db, userId, SecurityEventType.Login, TestUserAgent, TestIp, CancellationToken.None);
        }

        await using (var verifyScope = _factory.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.SecurityEvents.SingleAsync(e => e.UserId == userId && e.EventType == SecurityEventType.Login);
            Assert.Equal(DeviceTrackerService.HashIp(TestIp), evt.IpHash);
            Assert.DoesNotContain(TestIp, evt.IpHash);
            Assert.Equal("Chrome / Windows", evt.UserAgentLabel);
            Assert.NotEqual(DateTime.MinValue, evt.CreatedAt);
        }
    }

    [Fact]
    public async Task RemoveDeviceEndpoint_RecordsSessionRevokedWithLabel()
    {
        var userId = _factory.TestUserId;
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.RemoveRange(db.SecurityEvents.Where(e => e.UserId == userId));
            db.UserDevices.RemoveRange(db.UserDevices.Where(d => d.UserId == userId));
            await db.SaveChangesAsync();
            var tracker = new DeviceTrackerService();
            await tracker.RecordLoginAsync(db, userId, TestUserAgent, TestIp, CancellationToken.None);
        }

        var client = _factory.CreateAuthenticatedClient();
        var devices = await client.GetFromJsonAsync<List<DeviceListItem>>("/api/me/devices");
        Assert.NotNull(devices);
        var device = Assert.Single(devices);

        var response = await client.DeleteAsync($"/api/me/devices/{device.Id}");
        Assert.True(response.IsSuccessStatusCode);

        await using (var verifyScope = _factory.Services.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.SecurityEvents
                .OrderByDescending(e => e.CreatedAt)
                .FirstAsync(e => e.UserId == userId && e.EventType == SecurityEventType.SessionRevoked);
            Assert.Contains("Chrome / Windows", evt.MetadataJson);
        }
    }

    [Fact]
    public async Task CleanupJob_PurgesEventsOlderThanRetention()
    {
        var userId = _factory.TestUserId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.RemoveRange(db.SecurityEvents.Where(e => e.UserId == userId));
            await db.SaveChangesAsync();
            await SecurityEventService.AppendAsync(db, userId, SecurityEventType.Login, TestUserAgent, TestIp, CancellationToken.None);

            // Backdate one row past the 90-day window — CreatedAt is set by the entity default.
            await db.SecurityEvents
                .Where(e => e.UserId == userId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.CreatedAt, DateTime.UtcNow.AddDays(-91)));
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new SecurityEventCleanupJob(scopeFactory, NullLogger<SecurityEventCleanupJob>.Instance);
        await job.CleanupAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.SecurityEvents.CountAsync(e => e.UserId == userId);
            Assert.Equal(0, count);
        }
    }

    [Fact]
    public async Task CleanupJob_KeepsRecentEvents()
    {
        var userId = _factory.TestUserId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.RemoveRange(db.SecurityEvents.Where(e => e.UserId == userId));
            await db.SaveChangesAsync();
            await SecurityEventService.AppendAsync(db, userId, SecurityEventType.Login, TestUserAgent, TestIp, CancellationToken.None);
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new SecurityEventCleanupJob(scopeFactory, NullLogger<SecurityEventCleanupJob>.Instance);
        await job.CleanupAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var count = await db.SecurityEvents.CountAsync(e => e.UserId == userId);
            Assert.Equal(1, count);
        }
    }

    [Fact]
    public async Task Export_IncludesSecurityEvents()
    {
        var userId = _factory.TestUserId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.RemoveRange(db.SecurityEvents.Where(e => e.UserId == userId));
            await db.SaveChangesAsync();
            await SecurityEventService.AppendAsync(db, userId, SecurityEventType.DeviceAdded, TestUserAgent, TestIp, CancellationToken.None);
        }

        var client = _factory.CreateAuthenticatedClient();
        var export = await client.GetFromJsonAsync<ExportPayload>("/api/me/export");
        Assert.NotNull(export);
        var evt = Assert.Single(export.SecurityEvents);
        Assert.Equal(nameof(SecurityEventType.DeviceAdded), evt.Type);
        Assert.Equal("Chrome / Windows", evt.UserAgentLabel);
    }

    private sealed class DeviceListItem
    {
        public Guid Id { get; set; }
    }

    private sealed class ExportPayload
    {
        public List<ExportSecurityEvent> SecurityEvents { get; set; } = [];
    }

    private sealed class ExportSecurityEvent
    {
        public string Type { get; set; } = string.Empty;
        public string UserAgentLabel { get; set; } = string.Empty;
    }
}