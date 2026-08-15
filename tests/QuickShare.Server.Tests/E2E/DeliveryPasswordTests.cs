using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Share password on deliveries (issue #44): set at upload, BCrypt-hashed (never
/// plaintext), gate on /d/{code} before download, lockout on brute force, preview
/// gated.
/// </summary>
public sealed class DeliveryPasswordTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "password-test.bin";
    private const string TestContentType = "application/octet-stream";
    private const long TestFileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public DeliveryPasswordTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Upload_HashesPassword_NeverStoresPlaintext()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var shareCode = await UploadFileAsync(client, password: "s3cret!word");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.ShareCode == shareCode);

        Assert.NotNull(delivery.PasswordHash);
        Assert.NotEqual("s3cret!word", delivery.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("s3cret!word", delivery.PasswordHash));
    }

    [Fact]
    public async Task Download_WithoutPassword_Is403()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadPage_PasswordDelivery_RendersPasswordGate_FileHidden()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var html = await client.GetStringAsync($"/d/{shareCode}");

        // The password gate is the initial (visible) phase; the file card stays hidden.
        Assert.Contains("id=\"download-phase-password\" class=\"db-text-center ", html);
        Assert.Contains("id=\"download-phase-file\" class=\"hidden\"", html);
    }

    [Fact]
    public async Task Download_WrongPassword_Is403()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}?password=wrong");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Download_CorrectPassword_Redirects()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}?password=s3cret!word");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task VerifyPassword_Correct_ReturnsTrue()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-password", new { password = "s3cret!word" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<VerifyPasswordResult>();
        Assert.True(body?.Valid == true);
    }

    [Fact]
    public async Task VerifyPassword_Wrong_Is400()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-password", new { password = "wrong" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task VerifyPassword_FiveWrong_LocksDelivery()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-password", new { password = "wrong" });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        // Sixth attempt — locked, even with the correct password.
        var locked = await client.PostAsJsonAsync($"/api/deliveries/{shareCode}/verify-password", new { password = "s3cret!word" });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);
        Assert.True(locked.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Preview_PasswordGated_Is403()
    {
        var shareCode = await SeedDeliveryAsync(BCrypt.Net.BCrypt.HashPassword("s3cret!word"));

        var client = NoRedirectClient();
        var response = await client.GetAsync($"/api/downloads/{shareCode}/preview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient NoRedirectClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    private static async Task<string> UploadFileAsync(HttpClient client, string? password)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
            Password = password,
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        Assert.NotNull(commit);
        return commit.ShareCode;
    }

    private async Task<string> SeedDeliveryAsync(string? passwordHash)
    {
        var shareCode = $"pwd{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = _factory.TestUserId,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            TotalBytes = TestFileSize,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 5,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            PasswordHash = passwordHash,
        });
        await db.SaveChangesAsync();
        return shareCode;
    }

    private sealed class VerifyPasswordResult
    {
        public bool Valid { get; set; }
    }
}
