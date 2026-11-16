using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Upload-from-URL (issue #57): Pro-gated, SSRF-safe server fetch that lands a delivery.
/// The fetcher is stubbed in the factory — network + SSRF are unit-tested in IpAddressSafetyTests.
/// </summary>
public sealed class UploadFromUrlTests : IClassFixture<TestWebAppFactory>
{
    private const string UploadFromUrlApi = "/api/upload-from-url";

    private readonly TestWebAppFactory _factory;

    public UploadFromUrlTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record UploadFromUrlResponse(string ShareCode, string ShareLink, DateTime ExpiresAt);

    private static async Task<string> SeedAsync(TestWebAppFactory factory, string plan)
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await factory.SeedUserAsync(providerKey, plan);
        return providerKey;
    }

    [Fact]
    public async Task FreeUser_IsRejected()
    {
        var providerKey = await SeedAsync(_factory, PlanTierLimits.Free);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync(UploadFromUrlApi, new { Url = "https://example.com/file.bin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ProUser_InvalidScheme_IsRejected()
    {
        var providerKey = await SeedAsync(_factory, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync(UploadFromUrlApi, new { Url = "ftp://example.com/file.bin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProUser_ValidUrl_CreatesDelivery()
    {
        var providerKey = await SeedAsync(_factory, PlanTierLimits.Pro);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync(UploadFromUrlApi, new { Url = "https://example.com/file.bin" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UploadFromUrlResponse>();
        Assert.NotNull(result);
        Assert.False(string.IsNullOrEmpty(result.ShareCode));
        Assert.Contains("/d/", result.ShareLink);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = await db.Deliveries
            .AsNoTracking()
            .SingleAsync(d => d.ShareCode == result.ShareCode);

        Assert.Equal(DeliverySources.Url, delivery.Source);
        Assert.Equal("remote.bin", delivery.FileName);
        Assert.Equal(DeliveryStatus.Active, delivery.Status);
    }
}
