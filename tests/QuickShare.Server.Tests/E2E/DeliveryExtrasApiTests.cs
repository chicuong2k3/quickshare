using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Remaining delivery-related endpoints: branding, abuse reports,
/// password/OTP verify edge cases, and P2P share creation via API.
/// </summary>
public sealed class DeliveryExtrasApiTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public DeliveryExtrasApiTests(TestWebAppFactory factory) => _factory = factory;

    // ── Brand ──

    [Fact]
    public async Task GetBrand_UnknownCode_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/deliveries/no-such-code/brand");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBrand_ExistingDelivery_ReturnsBrandFields()
    {
        var (_, shareCode) = await SeedDeliveryAsync(d => d.BrandMessage = "hello brand");

        var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/deliveries/{shareCode}/brand");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("hello brand", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SetBrand_FreePlanUser_Returns403()
    {
        var (deliveryId, _) = await SeedDeliveryAsync(_ => { });

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync(
            $"/api/deliveries/{deliveryId}/brand", new { Message = "custom" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetBrand_NotOwnDelivery_Returns403()
    {
        var (deliveryId, _) = await SeedDeliveryAsync(_ => { }, senderId: Guid.NewGuid());

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PutAsJsonAsync(
            $"/api/deliveries/{deliveryId}/brand", new { Message = "custom" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Report / takedown ──

    [Fact]
    public async Task ReportDelivery_MarksTakedown()
    {
        var (deliveryId, shareCode) = await SeedDeliveryAsync(_ => { });

        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{shareCode}/report", new { Reason = "abuse" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == deliveryId);
        Assert.Equal("Takedown", delivery.Status);
    }

    [Fact]
    public async Task ReportDelivery_MissingReason_Returns400()
    {
        var (_, shareCode) = await SeedDeliveryAsync(_ => { });
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{shareCode}/report", new { Reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ReportDelivery_UnknownCode_Returns404()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/deliveries/no-such-code/report", new { Reason = "abuse" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Password verify (no password set ⇒ always valid) ──

    [Fact]
    public async Task VerifyPassword_DeliveryWithoutPassword_ReturnsValid()
    {
        var (_, shareCode) = await SeedDeliveryAsync(_ => { });
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/api/deliveries/{shareCode}/verify", new { Password = "anything" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("true", await response.Content.ReadAsStringAsync());
    }

    // ── Uploads: abort ──

    [Fact]
    public async Task AbortUpload_RemovesUploadSession()
    {
        var client = _factory.CreateAuthenticatedClient();
        var start = await (await client.PostAsJsonAsync("/api/uploads/start-r2", new
        {
            FileName = "abort-me.bin",
            FileSize = 100,
            ContentType = "application/octet-stream",
            PartNumbers = new[] { 1 },
        })).Content.ReadFromJsonAsync<StartR2Json>();
        Assert.NotNull(start);

        var abort = await client.PostAsJsonAsync("/api/uploads/abort-r2", new
        {
            start.DeliveryId,
            start.UploadId,
            start.ObjectKey,
        });
        Assert.Equal(HttpStatusCode.OK, abort.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.UploadSessions.FindAsync(start.DeliveryId);
        Assert.Null(session);
    }

    // ── P2P share API ──

    [Fact]
    public async Task CreateP2PShare_ReturnsShareLink()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "p2p.bin",
            FileSize = 123,
            MimeType = "application/octet-stream",
            BaseUrl = "http://localhost",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("/p2p/d/", body);
    }

    [Fact]
    public async Task CreateP2PShare_MissingFileName_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/shares/p2p", new
        {
            FileName = "",
            FileSize = 123,
            MimeType = "application/octet-stream",
            BaseUrl = "http://localhost",
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record StartR2Json(Guid DeliveryId, string UploadId, string ObjectKey);

    private async Task<(Guid Id, string ShareCode)> SeedDeliveryAsync(Action<Delivery> mutate, Guid? senderId = null)
    {
        var shareCode = $"ex-{Guid.NewGuid():N}"[..12];
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = senderId ?? _factory.TestUserId,
            FileName = "extras.bin",
            ContentType = "application/octet-stream",
            FileSize = 10,
            TotalBytes = 10,
            ChunkCount = 1,
            ShareCode = shareCode,
            MaxDownloads = 100,
            StoragePath = $"stub/{shareCode}",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
        };
        mutate(delivery);
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return (delivery.Id, shareCode);
    }
}
