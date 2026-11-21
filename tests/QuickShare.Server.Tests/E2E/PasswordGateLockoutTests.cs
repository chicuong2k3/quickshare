using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Password gate lockout (issue #148): repeated wrong passwords lock the delivery,
/// the lock answers 423 with Retry-After, and a correct verification clears it.
/// </summary>
public sealed class PasswordGateLockoutTests : IClassFixture<TestWebAppFactory>
{
    private const string Password = "correct-password";
    private const string Wrong = "wrong";
    private readonly TestWebAppFactory _factory;

    public PasswordGateLockoutTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task FiveBadPasswords_LockTheDelivery_ThenLockEscalates()
    {
        var code = await SeedAsync();
        using var client = Client();

        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using var locked = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.True(locked.Headers.TryGetValues("Retry-After", out var values));
        Assert.True(int.TryParse(values.First(), out var seconds) && seconds is > 0 and <= 900);

        // Even the right password is refused while the lock stands.
        using var duringLock = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
        Assert.Equal(HttpStatusCode.Locked, duringLock.StatusCode);
    }

    [Fact]
    public async Task CorrectPassword_ResetsTheFailureCount()
    {
        var code = await SeedAsync();
        using var client = Client();

        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
            {
                using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            }

            using var correct = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
            Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        }
    }

    [Fact]
    public async Task LockedDownloadEndpoint_Returns423_WithRetryAfter()
    {
        var code = await SeedAsync();
        using var client = Client();

        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
            await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });

        using var download = await client.GetAsync($"/api/downloads/{code}?password={Password}");
        Assert.Equal(HttpStatusCode.Locked, download.StatusCode);
        Assert.True(download.Headers.Contains("Retry-After"));
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    private async Task<string> SeedAsync()
    {
        var code = $"gate{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = "gated.bin",
            ContentType = "application/octet-stream",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = code,
            MaxDownloads = 5,
            StoragePath = $"stub/{code}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
        });
        await db.SaveChangesAsync();
        return code;
    }
}
