using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Querying;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Sender-side delivery management: revoke (#322), rename (#318), expiry calendar
/// (#232), and list filter/search (#214).
/// </summary>
public sealed class DeliveryManageTests : IClassFixture<TestWebAppFactory>
{
    private const string DeliveriesApi = "/api/deliveries";

    private readonly TestWebAppFactory _factory;

    public DeliveryManageTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record DeliveryRow(Guid Id, string FileName, string ShareCode, string Status);

    private async Task<Delivery> SeedDeliveryAsync(Guid userId, string fileName = "report.pdf", string status = DeliveryStatus.Active, DateTime? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            FileName = fileName,
            ContentType = "application/pdf",
            FileSize = 1024,
            ShareCode = Guid.NewGuid().ToString("N")[..10],
            MaxDownloads = 5,
            Status = status,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    // ── Revoke (#322) ──

    [Fact]
    public async Task Revoke_ActiveDelivery_BlocksDownload_AndKeepsRow()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var revoke = await client.PostAsync($"{DeliveriesApi}/{delivery.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
            Assert.Equal(DeliveryStatus.Revoked, row.Status);
            Assert.Null(row.StoragePath);
        }

        var download = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}");
        Assert.Equal(HttpStatusCode.Gone, download.StatusCode);
    }

    [Fact]
    public async Task Revoke_SomeoneElsesDelivery_Returns404()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var attackerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(attackerKey);
        var delivery = await SeedDeliveryAsync(owner.Id);

        var attacker = _factory.CreateAuthenticatedClient(attackerKey);
        var response = await attacker.PostAsync($"{DeliveriesApi}/{delivery.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_Returns400()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, status: DeliveryStatus.Revoked);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsync($"{DeliveriesApi}/{delivery.Id}/revoke", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Rename (#318) ──

    [Fact]
    public async Task Rename_UpdatesFileName()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "old.pdf");
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/filename", new { FileName = "  new-name.pdf " });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal("new-name.pdf", row.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("path/name.pdf")]
    [InlineData("path\\name.pdf")]
    public async Task Rename_InvalidName_Returns400(string fileName)
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/filename", new { FileName = fileName });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Expiry calendar (#232) ──

    [Fact]
    public async Task Calendar_ReturnsIcsWithEventAndAlarm()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "photos.zip");

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}/calendar.ics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/calendar", response.Content.Headers.ContentType?.MediaType);

        var ics = await response.Content.ReadAsStringAsync();
        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("photos.zip", ics);
        Assert.Contains("BEGIN:VALARM", ics);
        Assert.Contains($"/d/{delivery.ShareCode}", ics);
    }

    [Fact]
    public async Task Calendar_NoExpiry_Returns404()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
            row.ExpiresAt = null;
            await db.SaveChangesAsync();
        }

        var response = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}/calendar.ics");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── List filter + search (#214) ──

    [Fact]
    public async Task List_FiltersByStatus_AndSearchesByName()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedDeliveryAsync(user.Id, fileName: "invoice-january.pdf");
        await SeedDeliveryAsync(user.Id, fileName: "holiday-photos.zip");
        await SeedDeliveryAsync(user.Id, fileName: "invoice-february.pdf", status: DeliveryStatus.Revoked);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var revoked = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>($"{DeliveriesApi}?status={DeliveryStatus.Revoked}");
        Assert.NotNull(revoked);
        Assert.Single(revoked.Items);
        Assert.Equal("invoice-february.pdf", revoked.Items[0].FileName);

        var search = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>($"{DeliveriesApi}?q=INVOICE");
        Assert.NotNull(search);
        Assert.Equal(2, search.Items.Count);

        var both = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>($"{DeliveriesApi}?q=invoice&status={DeliveryStatus.Active}");
        Assert.NotNull(both);
        Assert.Single(both.Items);
        Assert.Equal("invoice-january.pdf", both.Items[0].FileName);
    }
}
