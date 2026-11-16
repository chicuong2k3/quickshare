using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

public sealed class CombinedGateLockoutTests : IClassFixture<TestWebAppFactory>
{
    private const string Password = "correct-password";
    private const string Otp = "123456";
    private const string Wrong = "wrong";
    private readonly TestWebAppFactory _factory;

    public CombinedGateLockoutTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task MissingOtpGate_CannotResetPasswordFailures()
    {
        var code = await SeedAsync(withOtp: false);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
        {
            using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            using var noOtp = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Otp });
            Assert.Equal(HttpStatusCode.OK, noOtp.StatusCode);
        }

        using var lastBad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
        Assert.Equal(HttpStatusCode.BadRequest, lastBad.StatusCode);
        using var locked = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task SinglePasswordGate_StillResetsAfterCorrectVerification()
    {
        var code = await SeedAsync(withOtp: false);
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
    public async Task MissingPasswordGate_CannotResetOtpFailures()
    {
        var code = await SeedAsync(withOtp: true, withPassword: false);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
        {
            using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            using var noPassword = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
            Assert.Equal(HttpStatusCode.OK, noPassword.StatusCode);
        }

        using var lastBad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Wrong });
        Assert.Equal(HttpStatusCode.BadRequest, lastBad.StatusCode);
        using var locked = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Otp });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task SingleOtpGate_StillResetsAfterCorrectVerification()
    {
        var code = await SeedAsync(withOtp: true, withPassword: false);
        using var client = Client();
        for (var round = 0; round < 2; round++)
        {
            for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
            {
                using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Wrong });
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            }

            using var correct = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Otp });
            Assert.Equal(HttpStatusCode.OK, correct.StatusCode);
        }
    }

    [Fact]
    public async Task CorrectPassword_CannotResetWrongOtpFailures()
    {
        var code = await SeedAsync(withOtp: true);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var password = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
            Assert.Equal(HttpStatusCode.OK, password.StatusCode);
            using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using var locked = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Otp });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task CorrectOtp_CannotResetWrongPasswordFailures()
    {
        var code = await SeedAsync(withOtp: true);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var otp = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-otp", new { otp = Otp });
            Assert.Equal(HttpStatusCode.OK, otp.StatusCode);
            using var bad = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        using var locked = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    [Fact]
    public async Task SuccessfulCombinedDownload_ResetsEarlierFailures()
    {
        var code = await SeedAsync(withOtp: true);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
        {
            using var bad = await client.GetAsync($"/api/downloads/{code}?password={Password}&otp={Wrong}");
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        }

        using var success = await client.GetAsync($"/api/downloads/{code}?password={Password}&otp={Otp}");
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);

        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts - 1; i++)
        {
            using var bad = await client.GetAsync($"/api/downloads/{code}?password={Password}&otp={Wrong}");
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        }
        using var check = await client.PostAsJsonAsync($"/api/deliveries/{code}/verify-password", new { password = Password });
        Assert.Equal(HttpStatusCode.OK, check.StatusCode);
    }

    [Fact]
    public async Task CorrectPasswordInDownload_CannotResetWrongOtpFailures()
    {
        var code = await SeedAsync(withOtp: true);
        using var client = Client();
        for (var i = 0; i < OtpLockoutPolicy.MaxFailedAttempts; i++)
        {
            using var bad = await client.GetAsync($"/api/downloads/{code}?password={Password}&otp={Wrong}");
            Assert.Equal(HttpStatusCode.Forbidden, bad.StatusCode);
        }

        using var locked = await client.GetAsync($"/api/downloads/{code}?password={Password}&otp={Otp}");
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
    }

    private HttpClient Client() => _factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
    });

    private async Task<string> SeedAsync(bool withOtp, bool withPassword = true)
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
            PasswordHash = withPassword ? BCrypt.Net.BCrypt.HashPassword(Password) : null,
            OtpHash = withOtp ? BCrypt.Net.BCrypt.HashPassword(Otp) : null,
        });
        await db.SaveChangesAsync();
        return code;
    }
}
