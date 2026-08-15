using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Email;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

public sealed class SenderEmailConfirmTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public SenderEmailConfirmTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Confirm_ValidToken_MarksEmailConfirmed()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reloaded = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.True(reloaded.SenderEmailConfirmed);
    }

    [Fact]
    public async Task Confirm_ExpiredToken_Returns400()
    {
        var delivery = await SeedDeliveryAsync();
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id, DateTimeOffset.UtcNow.AddHours(-1));

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_AlreadyConfirmed_Returns400()
    {
        var delivery = await SeedDeliveryAsync(confirmed: true);
        var tokens = _factory.Services.GetRequiredService<SenderEmailTokens>();
        var token = tokens.CreateConfirmToken(delivery.Id);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync($"/confirm-sender-email/{token}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Confirm_GarbageToken_Returns400()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync("/confirm-sender-email/not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Delivery> SeedDeliveryAsync(bool confirmed = false)
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
            SenderEmailConfirmed = confirmed,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }
}
