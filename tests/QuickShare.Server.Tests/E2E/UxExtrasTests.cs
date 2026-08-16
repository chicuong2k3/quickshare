using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Batch-4 UX extras: QR label sheet (#250), plan comparison table (#299),
/// link-health strip (#202), delivery-page share button (#283), announcement banner (#282).
/// </summary>
public sealed class UxExtrasTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public UxExtrasTests(TestWebAppFactory factory) => _factory = factory;

    private async Task<Delivery> SeedDeliveryAsync(Guid userId, DateTime? expiresAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            SenderId = userId,
            FileName = "labelled.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            ShareCode = Guid.NewGuid().ToString("N")[..10],
            MaxDownloads = 5,
            Status = DeliveryStatus.Active,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(2),
            CreatedAt = DateTime.UtcNow,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery;
    }

    private static async Task<string> GetHtmxAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add(HtmxHeaders.Request, HtmxHeaders.RequestValue);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    // ── QR label sheet (#250) ──

    [Fact]
    public async Task Labels_RequiresAuth()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var response = await client.GetAsync(RoutePaths.AccountDeliveryLabels);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [Fact]
    public async Task Labels_RendersALabelPerActiveDelivery()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        // Shell renders instantly without rows.
        var shell = await (await client.GetAsync(RoutePaths.AccountDeliveryLabels)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("account-label__qr", shell);

        var loaded = await GetHtmxAsync(client, RoutePaths.AccountDeliveryLabels);
        Assert.Contains("account-label__qr", loaded);
        Assert.Contains($"/d/{delivery.ShareCode}", loaded);
    }

    // ── Plan comparison (#299) ──

    [Fact]
    public async Task Pricing_ShowsComparisonTable_WithRealLimits()
    {
        var response = await _factory.CreateClient().GetAsync(RoutePaths.Pricing);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("compare__table", html);
        Assert.Contains("GB", html);
    }

    // ── Link health strip (#202) ──

    [Fact]
    public async Task Deliveries_ShowsHealthStrip_WithExpiringSoonCount()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedDeliveryAsync(user.Id, expiresAt: DateTime.UtcNow.AddHours(3));
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await GetHtmxAsync(client, RoutePaths.AccountDeliveries);
        Assert.Contains("account-deliveries-health", html);
        Assert.Contains("account-deliveries-health__warn", html);
    }

    // ── Delivery-page share button (#283) ──

    [Fact]
    public async Task DownloadPage_HasShareButton()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);

        var html = await (await _factory.CreateClient().GetAsync($"/d/{delivery.ShareCode}")).Content.ReadAsStringAsync();
        Assert.Contains("dl-share-label", html);
        Assert.Contains("sharePage", html);
    }

    // ── Announcement banner (#282) ──

    [Fact]
    public async Task Announcement_WhenConfigured_RendersDismissibleBanner()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["QuickShare:Announcement:Id"] = "test-1",
                ["QuickShare:Announcement:TextEn"] = "Maintenance Saturday 02:00 UTC",
                ["QuickShare:Announcement:Level"] = "warning",
            })));

        var html = await (await factory.CreateClient().GetAsync(RoutePaths.Pricing)).Content.ReadAsStringAsync();
        Assert.Contains("site-announcement--warning", html);
        Assert.Contains("Maintenance Saturday", html);
        Assert.Contains("data-announcement-id=\"test-1\"", html);
    }

    [Fact]
    public async Task Announcement_Unconfigured_NoBanner()
    {
        var html = await (await _factory.CreateClient().GetAsync(RoutePaths.Pricing)).Content.ReadAsStringAsync();
        Assert.DoesNotContain("site-announcement", html);
    }
}
