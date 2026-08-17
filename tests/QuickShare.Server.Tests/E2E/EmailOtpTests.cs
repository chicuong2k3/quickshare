using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Email OTP gate (issue #291): the recipient requests a single-use emailed code, the
/// download verifies it (hashed, TTL, single-use), and resend is cooldown-gated.
/// </summary>
public sealed class EmailOtpTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public EmailOtpTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<string> SeedAsync(Action<Delivery> mutate)
    {
        var shareCode = $"eotp{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "gated.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 10,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
        };
        mutate(delivery);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return shareCode;
    }

    [Fact]
    public async Task Request_DisabledBySender_Returns400()
    {
        var shareCode = await SeedAsync(_ => { });
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/request-email-otp", new { Email = "a@b.com" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_StoresHashedCode()
    {
        var shareCode = await SeedAsync(d => d.EmailOtpEnabled = true);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/request-email-otp", new { Email = "a@b.com" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
        Assert.NotNull(delivery.EmailOtpHash);
        Assert.Equal("a@b.com", delivery.EmailOtpEmail);
        Assert.True(delivery.EmailOtpExpiresAt > DateTime.UtcNow);
        Assert.Null(delivery.EmailOtpUsedAt);
    }

    [Fact]
    public async Task Request_WithinCooldown_Returns429()
    {
        var shareCode = await SeedAsync(d => d.EmailOtpEnabled = true);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/request-email-otp", new { Email = "a@b.com" });
        var second = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/request-email-otp", new { Email = "a@b.com" });

        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Download_ValidCode_Redirects_AndMarksUsed()
    {
        var shareCode = await SeedAsync(d =>
        {
            d.EmailOtpEnabled = true;
            d.EmailOtpHash = BCrypt.Net.BCrypt.HashPassword("123456");
            d.EmailOtpEmail = "recipient@example.com";
            d.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var url = $"/api/downloads/{shareCode}?emailOtp=123456&email=recipient@example.com";
        var first = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.SingleAsync(d => d.ShareCode == shareCode);
        Assert.NotNull(delivery.EmailOtpUsedAt);
    }

    [Fact]
    public async Task Download_ExpiredCode_Returns403()
    {
        var shareCode = await SeedAsync(d =>
        {
            d.EmailOtpEnabled = true;
            d.EmailOtpHash = BCrypt.Net.BCrypt.HashPassword("123456");
            d.EmailOtpEmail = "recipient@example.com";
            d.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp=123456&email=recipient@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_InvalidCode_Returns403()
    {
        var shareCode = await SeedAsync(d =>
        {
            d.EmailOtpEnabled = true;
            d.EmailOtpHash = BCrypt.Net.BCrypt.HashPassword("123456");
            d.EmailOtpEmail = "recipient@example.com";
            d.EmailOtpExpiresAt = DateTime.UtcNow.AddMinutes(10);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

        var response = await client.GetAsync($"/api/downloads/{shareCode}?emailOtp=000000&email=recipient@example.com");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
