using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Features.Users.Shared;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// New-device login tracking (issue #149): fingerprinting, first-seen alert
/// trigger, and the device list/removal endpoints.
/// </summary>
public sealed class DeviceTrackerTests : IClassFixture<TestWebAppFactory>
{
    private const string TestUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/126.0 Safari/537.36";

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly TestWebAppFactory _factory;

    public DeviceTrackerTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task SameFingerprint_IsNotNew_OnSecondLogin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracker = new DeviceTrackerService();
        var userId = _factory.TestUserId;

        db.UserDevices.RemoveRange(db.UserDevices.Where(d => d.UserId == userId));
        await db.SaveChangesAsync();

        var (first, firstIsNew) = await tracker.RecordLoginAsync(db, userId, TestUserAgent, "1.2.3.4", CancellationToken.None);
        Assert.True(firstIsNew);

        var (second, secondIsNew) = await tracker.RecordLoginAsync(db, userId, TestUserAgent, "1.2.3.4", CancellationToken.None);
        Assert.False(secondIsNew);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task DifferentIpOrAgent_IsANewDevice()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracker = new DeviceTrackerService();
        var userId = _factory.TestUserId;

        await tracker.RecordLoginAsync(db, userId, TestUserAgent, "1.2.3.4", CancellationToken.None);
        var (_, differentIpIsNew) = await tracker.RecordLoginAsync(db, userId, TestUserAgent, "5.6.7.8", CancellationToken.None);
        Assert.True(differentIpIsNew);

        var (_, differentAgentIsNew) = await tracker.RecordLoginAsync(db, userId, "Mozilla/5.0 (iPhone; CPU iPhone OS 17_0) Safari/604.1", "1.2.3.4", CancellationToken.None);
        Assert.True(differentAgentIsNew);
    }

    [Fact]
    public async Task Fingerprint_NeverContainsRawIpOrAgent()
    {
        var fingerprint = DeviceTrackerService.ComputeFingerprint(TestUserAgent, "203.0.113.7");
        var ipHash = DeviceTrackerService.HashIp("203.0.113.7");

        Assert.DoesNotContain("203.0.113.7", fingerprint);
        Assert.DoesNotContain("Chrome", fingerprint);
        Assert.NotEqual("203.0.113.7", ipHash);
    }

    [Fact]
    public async Task DeviceListEndpoint_ReturnsOnlyOwnDevices()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracker = new DeviceTrackerService();

        db.UserDevices.RemoveRange(db.UserDevices.Where(d => d.UserId == _factory.TestUserId));
        await db.SaveChangesAsync();

        await tracker.RecordLoginAsync(db, _factory.TestUserId, TestUserAgent, "1.2.3.4", CancellationToken.None);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.GetAsync("/api/me/devices");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        var devices = System.Text.Json.JsonSerializer.Deserialize<List<DeviceListItem>>(body, JsonOptions);
        Assert.NotNull(devices);
        Assert.Single(devices);
        Assert.Equal("Chrome / Windows", devices[0].UserAgentLabel);
    }

    [Fact]
    public async Task RemoveDeviceEndpoint_DeletesIt()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracker = new DeviceTrackerService();

        db.UserDevices.RemoveRange(db.UserDevices.Where(d => d.UserId == _factory.TestUserId));
        await db.SaveChangesAsync();

        var (device, _) = await tracker.RecordLoginAsync(db, _factory.TestUserId, TestUserAgent, "1.2.3.4", CancellationToken.None);

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.DeleteAsync($"/api/me/devices/{device.Id}");
        Assert.True(response.IsSuccessStatusCode);

        var after = await client.GetFromJsonAsync<List<DeviceListItem>>("/api/me/devices");
        Assert.NotNull(after);
        Assert.Empty(after);
    }

    [Fact]
    public async Task RemoveOthersDevice_Is404()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        var tracker = new DeviceTrackerService();
        var (device, _) = await tracker.RecordLoginAsync(db, _factory.TestUserId, TestUserAgent, "1.2.3.4", CancellationToken.None);

        var otherUser = await _factory.SeedUserAsync(Guid.NewGuid().ToString());
        var client = _factory.CreateAuthenticatedClient(otherUser.Id.ToString());
        var response = await client.DeleteAsync($"/api/me/devices/{device.Id}");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class DeviceListItem
    {
        public Guid Id { get; set; }
        public string UserAgentLabel { get; set; } = string.Empty;
        public DateTime FirstSeenAt { get; set; }
        public DateTime LastSeenAt { get; set; }
    }
}
