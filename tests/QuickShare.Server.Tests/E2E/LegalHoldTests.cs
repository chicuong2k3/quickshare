using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.Bulk;
using QuickShare.Features.Deliveries.LegalHold;
using QuickShare.Features.Deliveries.List;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// Legal hold (issue #266): a held delivery is frozen against every deletion path —
/// trash, purge, bulk trash, the expiry sweep's object delete, the trashed/expired
/// cleanup sweeps, and account deletion. The hold never expires on its own; only an
/// owner lifts it, and both transitions land on the security timeline.
///
/// The invariant these tests defend is fail-closed retention: if a code path can
/// destroy bytes, a hold must stop it. Each deletion path gets its own case, because
/// "one path forgot to check" is exactly the bug that loses evidence.
/// </summary>
public sealed class LegalHoldTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "contract-dispute.pdf";
    private const string TestContentType = "application/pdf";
    private const long TestFileSize = 2048;
    private const string CaseReference = "CASE-2026-118";

    private readonly TestWebAppFactory _factory;

    public LegalHoldTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Place_FreezesDeliveryAndAuditsTransition()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/legal-hold",
            new PlaceLegalHoldRequest { Reason = CaseReference });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LegalHoldResponse>();
        Assert.NotNull(payload);
        Assert.Equal(delivery, payload.DeliveryId);
        Assert.NotNull(payload.HeldAt);
        Assert.Equal(CaseReference, payload.Reason);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.NotNull(row.LegalHoldAt);
        Assert.Equal(CaseReference, row.LegalHoldReason);
        Assert.Equal(_factory.TestUserId, row.LegalHoldById);

        var audit = await db.SecurityEvents.AsNoTracking()
            .Where(e => e.UserId == _factory.TestUserId && e.EventType == SecurityEventType.LegalHoldPlaced)
            .ToListAsync();
        Assert.Contains(audit, e => e.MetadataJson != null && e.MetadataJson.Contains(CaseReference, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Place_WithoutCaseReference_Returns400AndLeavesRowFree()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        foreach (var reason in new[] { null, string.Empty, "   " })
        {
            var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/legal-hold",
                new PlaceLegalHoldRequest { Reason = reason });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.Null(row.LegalHoldAt);
    }

    [Fact]
    public async Task Place_Twice_Returns409AndKeepsOriginalReference()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        await PlaceHoldAsync(client, delivery);

        var second = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/legal-hold",
            new PlaceLegalHoldRequest { Reason = "CASE-OTHER" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.Equal(CaseReference, row.LegalHoldReason);
    }

    [Fact]
    public async Task Place_NotOwner_Returns404()
    {
        var otherUserId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherUserId);
        var foreignDelivery = await SeedDeliveryAsync(senderId: Guid.Parse(otherUserId));

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/deliveries/{foreignDelivery}/legal-hold",
            new PlaceLegalHoldRequest { Reason = CaseReference });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Trash_HeldDelivery_Returns409()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);
        await PlaceHoldAsync(client, delivery);

        var response = await client.PostAsync($"/api/deliveries/{delivery}/delete", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
        Assert.Null(row.DeletedAt);
        Assert.Equal(DeliveryStatus.Active, row.Status);
    }

    [Fact]
    public async Task Purge_HeldDelivery_Returns409AndKeepsRow()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);
        await PlaceHoldAsync(client, delivery);

        var response = await client.DeleteAsync($"/api/deliveries/{delivery}/purge");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == delivery));
    }

    [Fact]
    public async Task BulkTrash_SkipsHeldAndTrashesTheRest()
    {
        var client = _factory.CreateAuthenticatedClient();
        var held = await UploadFileAsync(client);
        var free = await UploadFileAsync(client);
        await PlaceHoldAsync(client, held);

        var response = await client.PostAsJsonAsync("/api/deliveries/bulk", new BulkDeliveriesRequest
        {
            Ids = [held, free],
            Action = "trash",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<BulkDeliveriesResponse>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload.Affected);
        Assert.Equal(1, payload.Skipped);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == held)).DeletedAt);
        Assert.NotNull((await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == free)).DeletedAt);
    }

    [Fact]
    public async Task AccountDeletion_RefusesWhileAnyDeliveryIsHeld()
    {
        // A dedicated account: this case deletes it, and the shared test user is needed
        // by every other case in the class.
        var ownerId = Guid.NewGuid().ToString();
        var owner = await _factory.SeedUserAsync(ownerId);
        var client = _factory.CreateAuthenticatedClient(ownerId);
        var delivery = await UploadFileAsync(client);
        await PlaceHoldAsync(client, delivery);

        var response = await client.PostAsJsonAsync("/api/me/delete", new { ConfirmEmail = owner.Email });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.Users.AsNoTracking().AnyAsync(u => u.Id == owner.Id));
            Assert.True(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == delivery));
        }

        // Lifting the hold clears the block — the refusal is about the hold, not the account.
        var lift = await client.PostAsync($"/api/deliveries/{delivery}/legal-hold/lift", null);
        Assert.Equal(HttpStatusCode.OK, lift.StatusCode);

        var afterLift = await client.PostAsJsonAsync("/api/me/delete", new { ConfirmEmail = owner.Email });
        Assert.Equal(HttpStatusCode.NoContent, afterLift.StatusCode);

        await using (var verify = _factory.Services.CreateAsyncScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Users.AsNoTracking().AnyAsync(u => u.Id == owner.Id));
        }
    }

    [Fact]
    public async Task ExpirySweep_HeldRowKeepsItsBytes()
    {
        var heldStoragePath = $"held/{Guid.NewGuid():N}";
        var freeStoragePath = $"free/{Guid.NewGuid():N}";
        var held = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            expiresAt: DateTime.UtcNow.AddMinutes(-5),
            storagePath: heldStoragePath,
            legalHoldAt: DateTime.UtcNow);
        var free = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            expiresAt: DateTime.UtcNow.AddMinutes(-5),
            storagePath: freeStoragePath);

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var storage = new LocalDiskStorageProviderStub();
        var job = new DeliveryExpiryJob(scopeFactory, storage, NullLogger<DeliveryExpiryJob>.Instance);
        await job.ExpireDeliveriesAsync();

        Assert.DoesNotContain(heldStoragePath, storage.DeletedPrefixes);
        Assert.Contains(freeStoragePath, storage.DeletedPrefixes);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var heldRow = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == held);
        // Expiry still closes the link — the hold protects the bytes, not the access.
        Assert.Equal(DeliveryStatus.Expired, heldRow.Status);
        Assert.Equal(heldStoragePath, heldRow.StoragePath);
        Assert.Equal(DeliveryStatus.Expired, (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == free)).Status);
    }

    [Fact]
    public async Task CleanupSweep_HeldRowsSurviveTrashGraceAndExpiredRetention()
    {
        var heldTrashed = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            deletedAt: DateTime.UtcNow.AddDays(-3),
            legalHoldAt: DateTime.UtcNow);
        var freeTrashed = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            deletedAt: DateTime.UtcNow.AddDays(-3));
        var heldExpired = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            expiresAt: DateTime.UtcNow.AddDays(-90),
            status: DeliveryStatus.Expired,
            storagePath: $"held-old/{Guid.NewGuid():N}",
            legalHoldAt: DateTime.UtcNow);
        var freeExpired = await SeedDeliveryAsync(
            senderId: _factory.TestUserId,
            expiresAt: DateTime.UtcNow.AddDays(-90),
            status: DeliveryStatus.Expired,
            storagePath: $"free-old/{Guid.NewGuid():N}");

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var storage = new LocalDiskStorageProviderStub();
        var service = new DeliveryCleanupService(scopeFactory, storage, NullLogger<DeliveryCleanupService>.Instance);
        await service.CleanupAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == heldTrashed));
        Assert.False(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == freeTrashed));
        Assert.True(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == heldExpired));
        Assert.False(await db.Deliveries.AsNoTracking().AnyAsync(d => d.Id == freeExpired));
    }

    [Fact]
    public async Task Lift_ClearsHoldAndAudits_ThenSecondLiftReturns409()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);
        await PlaceHoldAsync(client, delivery);

        var lift = await client.PostAsync($"/api/deliveries/{delivery}/legal-hold/lift", null);
        Assert.Equal(HttpStatusCode.OK, lift.StatusCode);
        var payload = await lift.Content.ReadFromJsonAsync<LegalHoldResponse>();
        Assert.NotNull(payload);
        Assert.Null(payload.HeldAt);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == delivery);
            Assert.Null(row.LegalHoldAt);
            Assert.Null(row.LegalHoldReason);
            Assert.Null(row.LegalHoldById);

            // The released case reference survives on the timeline even though the row cleared it.
            var audit = await db.SecurityEvents.AsNoTracking()
                .Where(e => e.UserId == _factory.TestUserId && e.EventType == SecurityEventType.LegalHoldLifted)
                .ToListAsync();
            Assert.Contains(audit, e => e.MetadataJson != null && e.MetadataJson.Contains(CaseReference, StringComparison.Ordinal));
        }

        var again = await client.PostAsync($"/api/deliveries/{delivery}/legal-hold/lift", null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // And the freed row is deletable again — the hold was the only thing refusing.
        var trash = await client.PostAsync($"/api/deliveries/{delivery}/delete", null);
        Assert.Equal(HttpStatusCode.NoContent, trash.StatusCode);
    }

    [Fact]
    public async Task List_ExposesHoldStateAndReference()
    {
        var client = _factory.CreateAuthenticatedClient();
        var held = await UploadFileAsync(client);
        var free = await UploadFileAsync(client);
        await PlaceHoldAsync(client, held);

        var list = await client.GetFromJsonAsync<DeliveryListPayload>("/api/deliveries?limit=100");
        Assert.NotNull(list);

        var heldItem = Assert.Single(list.Items, i => i.Id == held);
        Assert.True(heldItem.IsUnderLegalHold);
        Assert.Equal(CaseReference, heldItem.LegalHoldReason);

        var freeItem = Assert.Single(list.Items, i => i.Id == free);
        Assert.False(freeItem.IsUnderLegalHold);
        Assert.Null(freeItem.LegalHoldReason);
    }

    [Fact]
    public async Task DeliveriesPage_HeldRowShowsBadgeAndLiftAction()
    {
        // Own account so the row set is exactly what this case seeded.
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey);
        var client = _factory.CreateAuthenticatedClient(providerKey);
        var delivery = await UploadFileAsync(client);

        // The badge marker, not the class prefix — `account-delivery__hold` also prefixes the button.
        var before = await GetPageAsync(client, "/account/deliveries");
        Assert.DoesNotContain("delivery.holdBadge", before, StringComparison.Ordinal);
        Assert.Contains("delivery.hold\"", before, StringComparison.Ordinal);

        await PlaceHoldAsync(client, delivery);

        var after = await GetPageAsync(client, "/account/deliveries");
        Assert.Contains("delivery.holdBadge", after, StringComparison.Ordinal);
        Assert.Contains("account-delivery__hold\"", after, StringComparison.Ordinal);
        // The case reference is the state a legal team reads — it must reach the markup.
        Assert.Contains(CaseReference, after, StringComparison.Ordinal);
        // Placing is replaced by lifting, so a held row cannot be double-held from the UI.
        Assert.Contains("delivery.holdLift", after, StringComparison.Ordinal);
        Assert.DoesNotContain("delivery.hold\"", after, StringComparison.Ordinal);
        // No raw resx keys — the copy is localized in both languages.
        Assert.DoesNotContain("deliveries_hold", after, StringComparison.Ordinal);
    }

    private static async Task<string> GetPageAsync(HttpClient client, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Export_ReportsOnlyHeldDeliveries()
    {
        var client = _factory.CreateAuthenticatedClient();
        var held = await UploadFileAsync(client);
        var free = await UploadFileAsync(client);
        await PlaceHoldAsync(client, held);

        string heldShareCode;
        string freeShareCode;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            heldShareCode = (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == held)).ShareCode;
            freeShareCode = (await db.Deliveries.AsNoTracking().SingleAsync(d => d.Id == free)).ShareCode;
        }

        var response = await client.GetAsync("/api/deliveries/legal-hold/export");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);

        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("share_code,file_name,file_size_bytes,held_at_utc,case_reference,status", csv, StringComparison.Ordinal);
        Assert.Contains(heldShareCode, csv, StringComparison.Ordinal);
        Assert.Contains(CaseReference, csv, StringComparison.Ordinal);
        Assert.DoesNotContain(freeShareCode, csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_QuotesEmbeddedQuoteInCaseReference()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/legal-hold",
            new PlaceLegalHoldRequest { Reason = "CASE \"A\", subpoena" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var csv = await client.GetStringAsync("/api/deliveries/legal-hold/export");
        // A raw quote or a bare comma would split the row and misalign every later column.
        Assert.Contains("\"CASE \"\"A\"\", subpoena\"", csv, StringComparison.Ordinal);
    }

    private async Task PlaceHoldAsync(HttpClient client, Guid delivery)
    {
        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/legal-hold",
            new PlaceLegalHoldRequest { Reason = CaseReference });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> SeedDeliveryAsync(
        Guid senderId,
        DateTime? expiresAt = null,
        DateTime? deletedAt = null,
        DateTime? legalHoldAt = null,
        string? storagePath = null,
        string status = DeliveryStatus.Active)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var delivery = new Delivery
        {
            Id = Guid.CreateVersion7(),
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            TotalBytes = TestFileSize,
            ChunkCount = 1,
            ShareCode = "hld" + Guid.NewGuid().ToString("N")[..8],
            Status = status,
            SenderId = senderId,
            MaxDownloads = 5,
            ExpiresAt = expiresAt,
            DeletedAt = deletedAt,
            StoragePath = storagePath,
            LegalHoldAt = legalHoldAt,
            LegalHoldReason = legalHoldAt is null ? null : CaseReference,
            LegalHoldById = legalHoldAt is null ? null : senderId,
        };
        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync();
        return delivery.Id;
    }

    private async Task<Guid> UploadFileAsync(HttpClient client)
    {
        var startResponse = await client.PostAsJsonAsync("/api/uploads/start-r2", new StartR2Request
        {
            FileName = TestFileName,
            FileSize = TestFileSize,
            ContentType = TestContentType,
            PartNumbers = [1],
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var start = await startResponse.Content.ReadFromJsonAsync<StartR2Response>();
        Assert.NotNull(start);

        var commitResponse = await client.PostAsJsonAsync("/api/uploads/commit-r2", new CommitR2Request
        {
            DeliveryId = start.DeliveryId,
            UploadId = start.UploadId,
            ObjectKey = start.ObjectKey,
            FileName = TestFileName,
            ContentType = TestContentType,
            FileSize = TestFileSize,
            Parts = [new R2PartEtag(1, "etag-1")],
        });
        Assert.Equal(HttpStatusCode.Created, commitResponse.StatusCode);
        return start.DeliveryId;
    }

    private sealed class DeliveryListPayload
    {
        public List<DeliveryItem> Items { get; set; } = [];
        public string? NextCursor { get; set; }
    }
}
