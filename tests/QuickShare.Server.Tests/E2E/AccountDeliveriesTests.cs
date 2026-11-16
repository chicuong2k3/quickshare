using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;
using QuickShare.Querying;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The /account/deliveries tab: the download gauge must track real downloads (issue #6's
/// "download once, refresh — count must increase"), and the list must page by cursor.
/// </summary>
public sealed class AccountDeliveriesTests : IClassFixture<TestWebAppFactory>
{
    private const string DeliveriesApi = "/api/deliveries";
    private const int SeedCount = 5;
    private const int PageSize = 2;

    private readonly TestWebAppFactory _factory;

    public AccountDeliveriesTests(TestWebAppFactory factory) => _factory = factory;

    private sealed record DeliveryRow(
        Guid Id, string FileName, string ContentType, long FileSize, string ShareCode,
        int DownloadCount, int MaxDownloads, DateTime? ExpiresAt, DateTime CreatedAt);

    private static async Task<Guid> SeedDeliveriesAsync(
        TestWebAppFactory factory, Guid userId, int count, int downloadCount = 0)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var created = DateTime.UtcNow;
        var firstId = Guid.Empty;

        for (var i = 0; i < count; i++)
        {
            var delivery = new Delivery
            {
                Id = Guid.CreateVersion7(),
                SenderId = userId,
                FileName = $"file-{i}.pdf",
                ContentType = "application/pdf",
                FileSize = 1024,
                ShareCode = Guid.NewGuid().ToString("N")[..10],
                MaxDownloads = 5,
                DownloadCount = downloadCount,
                Status = DeliveryStatus.Active,
                ExpiresAt = created.AddHours(2),
                // Distinct timestamps: the cursor is keyed on CreatedAt.
                CreatedAt = created.AddSeconds(i),
            };
            db.Deliveries.Add(delivery);
            if (i == count - 1) firstId = delivery.Id;
        }

        await db.SaveChangesAsync();
        return firstId;
    }

    [Fact]
    public async Task DeliveriesApi_PagesByCursor_WithoutRepeatingRows()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var newestId = await SeedDeliveriesAsync(_factory, user.Id, SeedCount);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var seen = new List<Guid>();
        string? cursor = null;
        var pages = 0;

        do
        {
            var url = cursor is null
                ? $"{DeliveriesApi}?limit={PageSize}"
                : $"{DeliveriesApi}?limit={PageSize}&cursor={cursor}";
            var page = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>(url);

            Assert.NotNull(page);
            Assert.True(page.Items.Count <= PageSize, "a page returned more rows than the limit");
            seen.AddRange(page.Items.Select(i => i.Id));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < SeedCount + 1);

        Assert.Null(cursor);
        Assert.Equal(SeedCount, seen.Count);
        Assert.Equal(seen.Count, seen.Distinct().Count());
        // Newest first.
        Assert.Equal(newestId, seen[0]);
    }

    [Fact]
    public async Task DeliveriesApi_OnlyReturnsCallersOwnDeliveries()
    {
        var mineKey = Guid.CreateVersion7().ToString();
        var theirsKey = Guid.CreateVersion7().ToString();
        var mine = await _factory.SeedUserAsync(mineKey);
        var theirs = await _factory.SeedUserAsync(theirsKey);
        await SeedDeliveriesAsync(_factory, mine.Id, 2);
        await SeedDeliveriesAsync(_factory, theirs.Id, 3);

        var client = _factory.CreateAuthenticatedClient(mineKey);
        var page = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>(DeliveriesApi);

        Assert.NotNull(page);
        Assert.Equal(2, page.Items.Count);
    }

    [Fact]
    public async Task DeliveriesApi_RequiresAuth()
    {
        var response = await _factory.CreateClient().GetAsync(DeliveriesApi);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeliveriesPage_ShowsGauge_AndReflectsDownloadCount()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        await SeedDeliveriesAsync(_factory, user.Id, 1, downloadCount: 1);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        // The direct response reflects 1 of 5 downloads.
        var loaded = await GetPageAsync(client, RoutePaths.AccountDeliveries);
        Assert.Contains("account-delivery__gauge", loaded);
        Assert.Contains("width:20%", loaded);

        // Download again, refresh — the count must increase, not serve a cached page (issue #6).
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = await db.Deliveries.FirstAsync(d => d.SenderId == user.Id);
            delivery.DownloadCount = 3;
            await db.SaveChangesAsync();
        }

        var refreshed = await GetPageAsync(client, RoutePaths.AccountDeliveries);
        Assert.Contains("width:60%", refreshed);
        Assert.DoesNotContain("width:20%", refreshed);
    }

    [Fact]
    public async Task DeliveriesPage_EmptyState_WhenNothingSent()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var html = await GetPageAsync(client, RoutePaths.AccountDeliveries);
        Assert.Contains("account-deliveries-empty", html);
        Assert.DoesNotContain("account-delivery__gauge", html);
    }

    [Fact]
    public async Task AccountPages_ShowTabs()
    {
        var client = _factory.CreateAuthenticatedClient();

        foreach (var url in new[] { RoutePaths.Account, RoutePaths.AccountBilling, RoutePaths.AccountDeliveries })
        {
            var response = await client.GetAsync(url);
            var html = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {html}");
            Assert.Contains("account-tabs", html);
            Assert.Contains(RoutePaths.AccountDeliveries, html);
        }
    }

    private static async Task<string> GetPageAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }
}
