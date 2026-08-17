using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Recipient extension requests (issue #233): anonymous recipients ask for more time on an
/// expired delivery, the sender is notified, and approve/reject extend or close the request.
/// </summary>
public sealed class ExtensionRequestTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public ExtensionRequestTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record RequestExtensionResponse(bool Requested);

    private async Task<(string ProviderKey, string ShareCode, Guid DeliveryId)> SeedAsync(
        bool allowRequests, DateTime? expiresAt, string status = DeliveryStatus.Active)
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var shareCode = "ext" + Guid.NewGuid().ToString("N")[..8];
        var deliveryId = Guid.CreateVersion7();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Deliveries.Add(new Delivery
        {
            Id = deliveryId,
            SenderId = user.Id,
            FileName = "expired.pdf",
            ContentType = "application/pdf",
            ShareCode = shareCode,
            Status = status,
            ExpiresAt = expiresAt,
            AllowExtensionRequests = allowRequests,
        });
        await db.SaveChangesAsync();

        return (providerKey, shareCode, deliveryId);
    }

    [Fact]
    public async Task Request_DisabledBySender_Returns400()
    {
        var (_, shareCode, _) = await SeedAsync(allowRequests: false, DateTime.UtcNow.AddDays(-1));
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_NotExpired_Returns400()
    {
        var (_, shareCode, _) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(1));
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Request_CreatesRequestAndNotifiesSender()
    {
        var (_, shareCode, deliveryId) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(-1));
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var request = await db.ExtensionRequests
            .SingleAsync(r => r.DeliveryId == deliveryId);
        Assert.Equal(ExtensionRequestStatus.Pending, request.Status);

        var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId);
        var notification = await db.Notifications
            .SingleAsync(n => n.UserId == delivery.SenderId && n.Type == NotificationType.ExtensionRequested);
        Assert.Equal("expired.pdf", notification.Title);
    }

    [Fact]
    public async Task Request_IsIdempotentPerRecipient()
    {
        var (_, shareCode, deliveryId) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(-1));
        var client = _factory.CreateClient();

        await client.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);
        await client.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.ExtensionRequests.CountAsync(r => r.DeliveryId == deliveryId);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Approve_ExtendsDelivery()
    {
        var (providerKey, shareCode, deliveryId) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(-1));
        var requestClient = _factory.CreateClient();
        await requestClient.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);

        Guid requestId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            requestId = (await db.ExtensionRequests.SingleAsync(r => r.DeliveryId == deliveryId)).Id;
        }

        var sender = _factory.CreateAuthenticatedClient(providerKey);
        var response = await sender.PostAsync($"/api/extension-requests/{requestId}/approve", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.SingleAsync(d => d.Id == deliveryId);
            Assert.True(delivery.ExpiresAt > DateTime.UtcNow);
            Assert.Equal(DeliveryStatus.Active, delivery.Status);
            var request = await db.ExtensionRequests.SingleAsync(r => r.DeliveryId == deliveryId);
            Assert.Equal(ExtensionRequestStatus.Approved, request.Status);
        }
    }

    [Fact]
    public async Task Reject_MarksRejected()
    {
        var (providerKey, shareCode, deliveryId) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(-1));
        var requestClient = _factory.CreateClient();
        await requestClient.PostAsync($"/api/deliveries/{shareCode}/extension-request", content: null);

        Guid requestId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            requestId = (await db.ExtensionRequests.SingleAsync(r => r.DeliveryId == deliveryId)).Id;
        }

        var sender = _factory.CreateAuthenticatedClient(providerKey);
        var response = await sender.PostAsJsonAsync($"/api/extension-requests/{requestId}/reject", new { Message = "no" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var request = await db.ExtensionRequests.SingleAsync(r => r.DeliveryId == deliveryId);
            Assert.Equal(ExtensionRequestStatus.Rejected, request.Status);
            Assert.Equal("no", request.RejectMessage);
        }
    }

    [Fact]
    public async Task List_OtherUsersDelivery_Returns404()
    {
        var (_, shareCode, deliveryId) = await SeedAsync(allowRequests: true, DateTime.UtcNow.AddDays(-1));
        var otherKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(otherKey);
        var other = _factory.CreateAuthenticatedClient(otherKey);

        var response = await other.GetAsync($"/api/deliveries/{deliveryId}/extension-requests");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
