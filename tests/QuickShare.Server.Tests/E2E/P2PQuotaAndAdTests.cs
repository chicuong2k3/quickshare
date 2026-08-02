using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using QuickShare.Infrastructure;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// E2E tests for the daily P2P upload quota feature:
/// - free/anonymous users limited to 3 P2P share links per day (per IP),
/// - paid plans unlimited,
/// - watching a rewarded ad unlocks +1 extra upload for the day,
/// - reward callback token verification and idempotency.
/// </summary>
public sealed class P2PQuotaAndAdTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public P2PQuotaAndAdTests(TestWebAppFactory factory) => _factory = factory;

    private static readonly StringContent SharePayload = new(
        """{"fileName":"test.bin","fileSize":1024,"mimeType":"application/octet-stream","baseUrl":"http://localhost"}""",
        Encoding.UTF8,
        "application/json");

    /// <summary>
    /// The app's <c>UseForwardedHeaders</c> trusts all proxies, so an
    /// <c>X-Forwarded-For</c> header is honored and drives the per-IP quota
    /// bucket. Each test uses a distinct IP so the shared InMemory DB does not
    /// bleed anonymous usage between tests.
    /// </summary>
    private HttpClient AnonymousClientWithIp(string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private async Task<HttpResponseMessage> CreateShareAsync(HttpClient client)
    {
        return await client.PostAsync("/api/shares/p2p", SharePayload);
    }

    // ── Anonymous (per-IP) daily limit ──

    [Fact]
    public async Task AnonymousUser_CanCreateThreeSharesPerDay_Then429()
    {
        using var client = AnonymousClientWithIp("203.0.113.1");
        for (var i = 0; i < 3; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await CreateShareAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        var body = await blocked.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("daily_p2p_upload_limit_reached", doc.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AnonymousQuota_IsPerIp()
    {
        using var clientA = AnonymousClientWithIp("203.0.113.10");
        using var clientB = AnonymousClientWithIp("203.0.113.20");

        // A exhausts its 3/day allowance.
        for (var i = 0; i < 3; i++)
        {
            var response = await CreateShareAsync(clientA);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var blockedA = await CreateShareAsync(clientA);
        Assert.Equal(HttpStatusCode.TooManyRequests, blockedA.StatusCode);

        // B is unaffected — different IP bucket.
        var responseB = await CreateShareAsync(clientB);
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
    }

    // ── Authenticated free user daily limit ──

    [Fact]
    public async Task FreeUser_CanCreateThreeSharesPerDay_Then429()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        for (var i = 0; i < 3; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await CreateShareAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        var body = await blocked.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.True(doc.RootElement.GetProperty("canUnlockWithAd").GetBoolean());
    }

    [Fact]
    public async Task ProUser_HasUnlimitedShares()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Pro);
        using var client = _factory.CreateAuthenticatedClient(userId);

        for (var i = 0; i < 6; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task StudioUser_HasUnlimitedShares()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Studio);
        using var client = _factory.CreateAuthenticatedClient(userId);

        for (var i = 0; i < 6; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    // ── Ad unlock extends the daily quota ──

    [Fact]
    public async Task FreeUser_AdReward_UnlocksFourthShare()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        // Exhaust the 3/day allowance.
        for (var i = 0; i < 3; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        var blocked = await CreateShareAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // Watch an ad → reward callback grants +1 P2P upload.
        var reward = await PostRewardAsync(client, $"token-{Guid.NewGuid():N}", 1);
        Assert.Equal(HttpStatusCode.OK, reward.StatusCode);
        var rewardBody = await reward.Content.ReadFromJsonAsync<RewardCallbackResponse>();
        Assert.NotNull(rewardBody);
        Assert.True(rewardBody!.Success);
        Assert.Equal(1, rewardBody.P2pUploadsUnlocked);

        // 4th share is now allowed.
        var fourth = await CreateShareAsync(client);
        Assert.Equal(HttpStatusCode.OK, fourth.StatusCode);
    }

    [Fact]
    public async Task FreeUser_WithoutAd_StaysBlockedOnFourth()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        for (var i = 0; i < 3; i++)
        {
            var response = await CreateShareAsync(client);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await CreateShareAsync(client);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    // ── Quota status endpoint ──

    [Fact]
    public async Task QuotaEndpoint_Anonymous_ReturnsLimitAndUsed()
    {
        using var client = AnonymousClientWithIp("198.51.100.7");

        var response = await client.GetAsync("/api/p2p/quota");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var quota = await response.Content.ReadFromJsonAsync<GetP2PQuotaResponse>();
        Assert.NotNull(quota);
        Assert.False(quota!.Unlimited);
        Assert.Equal(AdConstants.MaxDailyP2PUploads, quota.Limit);
        Assert.Equal(0, quota.Used);
        Assert.False(quota.CanUnlockWithAd);
    }

    [Fact]
    public async Task QuotaEndpoint_FreeUser_ReflectsUsage()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        var before = await client.GetFromJsonAsync<GetP2PQuotaResponse>("/api/p2p/quota");
        Assert.NotNull(before);
        Assert.Equal(0, before!.Used);

        await CreateShareAsync(client);

        var after = await client.GetFromJsonAsync<GetP2PQuotaResponse>("/api/p2p/quota");
        Assert.NotNull(after);
        Assert.Equal(1, after!.Used);
        Assert.Equal(AdConstants.MaxDailyP2PUploads - 1, after.Remaining);
    }

    [Fact]
    public async Task QuotaEndpoint_ProUser_ReturnsUnlimited()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Pro);
        using var client = _factory.CreateAuthenticatedClient(userId);

        var response = await client.GetFromJsonAsync<GetP2PQuotaResponse>("/api/p2p/quota");
        Assert.NotNull(response);
        Assert.True(response!.Unlimited);
    }

    // ── Reward callback: token verification & idempotency ──

    [Fact]
    public async Task RewardCallback_ValidToken_GrantsReward()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        var response = await PostRewardAsync(client, $"token-{Guid.NewGuid():N}", 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RewardCallbackResponse>();
        Assert.NotNull(body);
        Assert.True(body!.Success);
        Assert.Equal(100, body.QuotaAddedMb);
        Assert.Equal(1, body.P2pUploadsUnlocked);
    }

    [Fact]
    public async Task RewardCallback_DuplicateToken_NoDoubleGrant()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        var token = $"token-{Guid.NewGuid():N}";

        var first = await PostRewardAsync(client, token, 1);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await PostRewardAsync(client, token, 1);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var secondBody = await second.Content.ReadFromJsonAsync<RewardCallbackResponse>();
        Assert.NotNull(secondBody);
        Assert.True(secondBody!.Success);
        Assert.Equal(0, secondBody.QuotaAddedMb); // no double-count
        Assert.Equal(0, secondBody.P2pUploadsUnlocked);
        Assert.Contains("Duplicate", secondBody.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewardCallback_MissingToken_Returns400()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        var response = await PostRewardAsync(client, string.Empty, 1);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RewardCallback_Anonymous_Returns401()
    {
        using var client = _factory.CreateClient();

        var response = await PostRewardAsync(client, $"token-{Guid.NewGuid():N}", 1);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RewardCallback_DailyAdLimit_Returns429()
    {
        var userId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(userId, PlanTierLimits.Free);
        using var client = _factory.CreateAuthenticatedClient(userId);

        for (var i = 0; i < AdConstants.MaxDailyAds; i++)
        {
            var response = await PostRewardAsync(client, $"token-{Guid.NewGuid():N}", 1);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var blocked = await PostRewardAsync(client, $"token-{Guid.NewGuid():N}", 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
    }

    private static async Task<HttpResponseMessage> PostRewardAsync(HttpClient client, string token, int amount)
    {
        return await client.PostAsJsonAsync("/api/ads/reward-callback",
            new { token, rewardAmount = amount });
    }

    private sealed class RewardCallbackResponse
    {
        public bool Success { get; set; }
        public int QuotaAddedMb { get; set; }
        public int QuotaNowMb { get; set; }
        public int AdsRemainingToday { get; set; }
        public int P2pUploadsUnlocked { get; set; }
        public string? Error { get; set; }
    }

    private sealed class GetP2PQuotaResponse
    {
        public string Plan { get; set; } = string.Empty;
        public bool Unlimited { get; set; }
        public int Limit { get; set; }
        public int Used { get; set; }
        public int Unlocked { get; set; }
        public int Allowance { get; set; }
        public int Remaining { get; set; }
        public bool CanUnlockWithAd { get; set; }
    }
}
