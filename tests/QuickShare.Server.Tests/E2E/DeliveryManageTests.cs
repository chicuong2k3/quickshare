using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Features.Deliveries.Replace;
using QuickShare.Infrastructure.Storage;
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

    // ── Clone (#218) ──

    [Fact]
    public async Task Clone_ActiveDelivery_CreatesFreshLink()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "clone-me.pdf");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.FirstAsync(d => d.Id == delivery.Id);
            row.StoragePath = "stub/source-key";
            row.DownloadCount = 3;
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsync($"{DeliveriesApi}/{delivery.Id}/clone", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CloneJson>();
        Assert.NotNull(body);
        Assert.NotEqual(delivery.ShareCode, body.ShareCode);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clone = await verifyDb.Deliveries.AsNoTracking().FirstAsync(d => d.Id == body.Id);
        Assert.Equal("clone-me.pdf", clone.FileName);
        Assert.Equal(0, clone.DownloadCount);
        Assert.Equal(DeliveryStatus.Active, clone.Status);
        Assert.NotEqual("stub/source-key", clone.StoragePath);
    }

    [Fact]
    public async Task Clone_RevokedDelivery_Returns400()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, status: DeliveryStatus.Revoked);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsync($"{DeliveriesApi}/{delivery.Id}/clone", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record CloneJson(Guid Id, string ShareCode);

    // ── Expired archive grace period (#222) ──

    [Fact]
    public async Task Cleanup_KeepsRecentlyExpiredRows_PurgesOldOnes()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var recent = await SeedDeliveryAsync(user.Id, status: DeliveryStatus.Expired,
            expiresAt: DateTime.UtcNow.AddDays(-1));
        var ancient = await SeedDeliveryAsync(user.Id, status: DeliveryStatus.Expired,
            expiresAt: DateTime.UtcNow - DeliveryCleanupService.ExpiredRowRetention - TimeSpan.FromDays(1));

        var cleanup = new DeliveryCleanupService(
            _factory.Services.GetRequiredService<IServiceScopeFactory>(),
            _factory.Services.GetRequiredService<QuickShare.Infrastructure.Storage.IStorageProvider>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DeliveryCleanupService>.Instance);
        await cleanup.CleanupAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == recent.Id));
        Assert.Null(await db.Deliveries.AsNoTracking().FirstOrDefaultAsync(d => d.Id == ancient.Id));
    }

    // ── 404 page (#206) ──

    [Fact]
    public async Task UnknownRoute_Returns404_WithBrandedPage()
    {
        var response = await _factory.CreateClient().GetAsync("/definitely-not-a-real-page");
        // The catch-all route matches and returns 404; the branded page is rendered.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Custom alias (#311) ──

    [Fact]
    public async Task Alias_ProUser_SetsAlias_AndLinkResolvesByIt()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "aliased.pdf");
        var client = _factory.CreateAuthenticatedClient(providerKey);
        var alias = $"my-project-{Guid.NewGuid().ToString("N")[..6]}";

        var set = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/alias", new { Alias = alias.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        // Lookup by alias behaves like the share code (lowercased on store).
        var calendar = await _factory.CreateClient().GetAsync($"/api/downloads/{alias}/calendar.ics");
        Assert.Equal(HttpStatusCode.OK, calendar.StatusCode);
    }

    [Fact]
    public async Task Alias_FreeUser_Gets403()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/alias", new { Alias = "free-user-alias" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("ab")]
    [InlineData("Bad Slug!")]
    public async Task Alias_InvalidOrReserved_Gets400(string alias)
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/alias", new { Alias = alias });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Alias_Taken_Gets409_AndCheckReportsUnavailable()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var first = await SeedDeliveryAsync(user.Id);
        var second = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        var alias = $"taken-{Guid.NewGuid().ToString("N")[..6]}";

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"{DeliveriesApi}/{first.Id}/alias", new { Alias = alias })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PatchAsJsonAsync($"{DeliveriesApi}/{second.Id}/alias", new { Alias = alias })).StatusCode);

        var check = await client.GetFromJsonAsync<AliasCheckJson>($"{DeliveriesApi}/alias-check?alias={alias}");
        Assert.NotNull(check);
        Assert.False(check.Available);
    }

    private sealed record AliasCheckJson(bool Available);

    // ── Daily download cap (#227) ──

    [Fact]
    public async Task DailyLimit_Reached_Blocks_WithRetryTomorrow()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey, PlanTierLimits.Pro);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/daily-limit", new { Limit = 1 })).StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Deliveries.First(d => d.Id == delivery.Id).StoragePath = "stub/key";
            db.DownloadTrackings.Add(new DownloadTracking
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery.Id,
                IpAddress = "10.0.0.1",
                DownloadedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var download = await _factory.CreateClient().GetAsync($"/api/downloads/{delivery.ShareCode}");
        Assert.Equal(HttpStatusCode.TooManyRequests, download.StatusCode);
    }

    [Fact]
    public async Task DailyLimit_FreeUser_Gets403()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PatchAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/daily-limit", new { Limit = 10 });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Favorites (#242) ──

    [Fact]
    public async Task Favorite_TogglesAndFilters()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var starred = await SeedDeliveryAsync(user.Id, fileName: "starred.pdf");
        await SeedDeliveryAsync(user.Id, fileName: "plain.pdf");
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var toggle = await client.PatchAsync($"{DeliveriesApi}/{starred.Id}/favorite", null);
        Assert.Equal(HttpStatusCode.OK, toggle.StatusCode);
        var body = await toggle.Content.ReadFromJsonAsync<FavoriteJson>();
        Assert.NotNull(body);
        Assert.True(body.IsFavorite);

        var favorites = await client.GetFromJsonAsync<CursorPage<DeliveryRow>>($"{DeliveriesApi}?favorite=true");
        Assert.NotNull(favorites);
        Assert.Single(favorites.Items);
        Assert.Equal("starred.pdf", favorites.Items[0].FileName);

        // Toggle back off.
        var untoggle = await client.PatchAsync($"{DeliveriesApi}/{starred.Id}/favorite", null);
        var offBody = await untoggle.Content.ReadFromJsonAsync<FavoriteJson>();
        Assert.NotNull(offBody);
        Assert.False(offBody.IsFavorite);
    }

    private sealed record FavoriteJson(bool IsFavorite);

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

    // ── Bulk manage (#144) ──

    [Fact]
    public async Task Bulk_Trash_MovesAllOwnedDeliveriesToTrash()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var a = await SeedDeliveryAsync(user.Id, fileName: "a.pdf");
        var b = await SeedDeliveryAsync(user.Id, fileName: "b.pdf");
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync($"{DeliveriesApi}/bulk", new { Ids = new[] { a.Id, b.Id }, Action = "trash" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = await db.Deliveries.AsNoTracking().Where(d => new[] { a.Id, b.Id }.Contains(d.Id)).ToListAsync();
        Assert.All(rows, r => Assert.NotNull(r.DeletedAt));
    }

    [Fact]
    public async Task Bulk_Extend_ExtendsOwnedDelivery()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, expiresAt: DateTime.UtcNow.AddHours(5));
        var before = delivery.ExpiresAt!.Value;
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync($"{DeliveriesApi}/bulk", new { Ids = new[] { delivery.Id }, Action = "extend", Days = 7 });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.True(row.ExpiresAt > before);
    }

    [Fact]
    public async Task Bulk_SomeoneElsesDelivery_Returns404()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var attackerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(attackerKey);
        var delivery = await SeedDeliveryAsync(owner.Id);

        var attacker = _factory.CreateAuthenticatedClient(attackerKey);
        var response = await attacker.PostAsJsonAsync($"{DeliveriesApi}/bulk", new { Ids = new[] { delivery.Id }, Action = "trash" });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Purge forever (#157) ──

    [Fact]
    public async Task Purge_RemovesDeliveryPermanently()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.DeleteAsync($"{DeliveriesApi}/{delivery.Id}/purge");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Deliveries.AnyAsync(d => d.Id == delivery.Id));
    }

    [Fact]
    public async Task Purge_SomeoneElsesDelivery_Returns404()
    {
        var ownerKey = Guid.CreateVersion7().ToString();
        var attackerKey = Guid.CreateVersion7().ToString();
        var owner = await _factory.SeedUserAsync(ownerKey);
        await _factory.SeedUserAsync(attackerKey);
        var delivery = await SeedDeliveryAsync(owner.Id);

        var attacker = _factory.CreateAuthenticatedClient(attackerKey);
        var response = await attacker.DeleteAsync($"{DeliveriesApi}/{delivery.Id}/purge");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Replace file, keep link (#72) ──

    [Fact]
    public async Task Replace_UpdatesFileAndKeepsLink()
    {
        var providerKey = Guid.CreateVersion7().ToString();
        var user = await _factory.SeedUserAsync(providerKey);
        var delivery = await SeedDeliveryAsync(user.Id, fileName: "old.pdf");
        var originalCode = delivery.ShareCode;
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var startRes = await client.PostAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/replace/start", new
        {
            FileName = "new.pdf",
            FileSize = 2048L,
            ContentType = "application/pdf",
            PartNumbers = new[] { 1 }
        });
        Assert.Equal(HttpStatusCode.OK, startRes.StatusCode);
        var start = await startRes.Content.ReadFromJsonAsync<ReplaceDeliveryStartResponse>();
        Assert.NotNull(start);

        var commitRes = await client.PostAsJsonAsync($"{DeliveriesApi}/{delivery.Id}/replace/commit", new
        {
            SessionId = start.SessionId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            Parts = new[] { new R2PartEtag(1, "etag-1") }
        });
        Assert.Equal(HttpStatusCode.NoContent, commitRes.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().FirstAsync(d => d.Id == delivery.Id);
        Assert.Equal("new.pdf", row.FileName);
        Assert.Equal(2048, row.FileSize);
        Assert.Equal(originalCode, row.ShareCode);
        Assert.Equal(0, row.DownloadCount);
    }
}
