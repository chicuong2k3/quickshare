using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Server.Tests.E2E;

public sealed class SenderEmailUnsubscribeTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public SenderEmailUnsubscribeTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Unsubscribe_ValidToken_SetsConsentFalse()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateUnsubscribeToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/unsubscribe-sender/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.False(reloaded.SenderEmailConsent);
    }

    [Fact]
    public async Task Unsubscribe_ExpiredToken_Returns400()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateUnsubscribeToken(delivery.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/unsubscribe-sender/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_GarbageToken_Returns400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/unsubscribe-sender/not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Delivery> SeedDeliveryAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            TotalBytes = 1024,
            ChunkCount = 1,
            ShareCode = $"seed-{Guid.NewGuid():N}"[..12],
            MaxDownloads = 100,
            StoragePath = "stub/seed",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            SenderEmail = "sender@example.com",
            SenderEmailConsent = true,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }
}
