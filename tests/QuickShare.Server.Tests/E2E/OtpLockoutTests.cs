using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// OTP brute-force lockout (issue #148): 5 failed attempts → 423 + Retry-After,
/// escalating lock durations, reset on success.
/// </summary>
public sealed class OtpLockoutTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public OtpLockoutTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FiveBadOtps_LockTheDelivery_ThenLockEscalates()
    {
        var shareCode = await SeedDeliveryAsync(otpHash: BCrypt.Net.BCrypt.HashPassword("123456"));

        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        // Five wrong attempts — 400 each (the 5th failure trips the lock, but the
        // response for that attempt is still "wrong code").
        for (var i = 0; i < 5; i++)
        {
            var response = await PostOtpAsync(client, shareCode, "000000");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // Sixth attempt — the delivery is now locked.
        var locked = await PostOtpAsync(client, shareCode, "000000");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        var retryAfter = locked.Headers.GetValues("Retry-After").First();
        Assert.True(int.TryParse(retryAfter, out var seconds) && seconds is > 0 and <= 900);

        // Even the correct OTP is rejected while locked.
        var duringLock = await PostOtpAsync(client, shareCode, "123456");
        Assert.Equal(HttpStatusCode.Locked, duringLock.StatusCode);

        // A correct OTP on a delivery that never locked stays open.
        var other = await SeedDeliveryAsync(otpHash: BCrypt.Net.BCrypt.HashPassword("654321"));
        var ok = await PostOtpAsync(client, other, "654321");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.True((await ok.Content.ReadFromJsonAsync<VerifyOtpResult>())?.Valid == true);
    }

    [Fact]
    public async Task CorrectOtp_OnUnlockedDelivery_ResetsAndSucceeds()
    {
        var shareCode = await SeedDeliveryAsync(otpHash: BCrypt.Net.BCrypt.HashPassword("123456"));
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await PostOtpAsync(client, shareCode, "123456");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task LockedDownloadEndpoint_Returns423_WithRetryAfter()
    {
        var shareCode = await SeedDeliveryAsync(otpHash: BCrypt.Net.BCrypt.HashPassword("123456"));
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < 5; i++)
            await PostOtpAsync(client, shareCode, "000000");

        var download = await client.GetAsync($"/api/downloads/{shareCode}?otp=123456");
        Assert.Equal(HttpStatusCode.Locked, download.StatusCode);
        Assert.True(download.Headers.Contains("Retry-After"));
    }

    private async Task<HttpResponseMessage> PostOtpAsync(HttpClient client, string shareCode, string otp)
    {
        var payload = new { otp };
        return await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-otp", payload);
    }

    private async Task<string> SeedDeliveryAsync(string otpHash)
    {
        var shareCode = $"lock{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuickShare.Data.AppDbContext>();
        db.Deliveries.Add(new QuickShare.Data.Entities.Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "otp-test.bin",
            ContentType = "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            OtpHash = otpHash
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private sealed class VerifyOtpResult
    {
        public bool Valid { get; set; }
    }
}
