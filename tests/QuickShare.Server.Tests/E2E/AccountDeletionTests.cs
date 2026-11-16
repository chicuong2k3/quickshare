using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Account data export + deletion (issue #59): privacy rights for the sender.
/// </summary>
public sealed class AccountDeletionTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public AccountDeletionTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<Delivery> SeedDeliveryAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            FileName = "private.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            ShareCode = Guid.NewGuid().ToString("N")[..10],
            MaxDownloads = 3,
            Status = DeliveryStatus.Active,
            ExpiresAt = DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    [Fact]
    public async Task Export_ReturnsJsonWithProfileAndDeliveries()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.GetAsync("/api/me/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", response.Content.Headers.ContentType?.ToString());

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("private.pdf", body);
        Assert.Contains(TestWebAppFactory.TestUserEmail, body);
    }

    [Fact]
    public async Task Delete_WrongEmail_Returns400_AndKeepsAccount()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/me/delete", new { ConfirmEmail = "wrong@example.com" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.AnyAsync(u => u.Id == Guid.Parse(providerKey)));
    }

    [Fact]
    public async Task Delete_CorrectEmail_RemovesUserAndDeliveries()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/me/delete", new { ConfirmEmail = TestWebAppFactory.TestUserEmail });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Id == user.Id));
        Assert.False(await db.Deliveries.AnyAsync(d => d.Id == delivery.Id));
    }
}
