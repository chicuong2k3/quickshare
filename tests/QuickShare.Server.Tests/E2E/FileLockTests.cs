using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Deliveries.List;
using QuickShare.Features.Deliveries.Lock;
using QuickShare.Features.Deliveries.Replace;
using QuickShare.Features.Uploads.CommitR2;
using QuickShare.Features.Uploads.StartR2;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// File lock / checkout (issue #308): a sender can lock a delivery so only the
/// holder may replace the file; a lock held by another user blocks replacement
/// with a 423, the owner can force-break any lock (notifying the holder), and
/// expired locks are released by the expiry job.
/// </summary>
public sealed class FileLockTests : IClassFixture<TestWebAppFactory>
{
    private const string TestFileName = "locked.bin";
    private const string TestContentType = "application/octet-stream";
    private const long TestFileSize = 1024;

    private readonly TestWebAppFactory _factory;

    public FileLockTests(TestWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Lock_CreatesActiveLock()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock",
            new LockDeliveryRequest { DurationMinutes = 60, Reason = "preparing v2" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lockResponse = await response.Content.ReadFromJsonAsync<LockDeliveryResponse>();
        Assert.NotNull(lockResponse);
        Assert.NotEqual(Guid.Empty, lockResponse.LockId);
        Assert.Equal(_factory.TestUserId, lockResponse.LockedById);
        Assert.True(lockResponse.ExpiresAt > DateTime.UtcNow);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.FileLocks.SingleAsync(l => l.DeliveryId == delivery);
            Assert.Equal(_factory.TestUserId, row.LockedById);
            Assert.Equal("preparing v2", row.Reason);
            Assert.Null(row.ReleasedAt);
            Assert.Equal(lockResponse.ExpiresAt, row.ExpiresAt);
        }
    }

    [Fact]
    public async Task Lock_SelfLock_Idempotent()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var first = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstResponse = await first.Content.ReadFromJsonAsync<LockDeliveryResponse>();

        var second = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondResponse = await second.Content.ReadFromJsonAsync<LockDeliveryResponse>();

        Assert.NotNull(firstResponse);
        Assert.NotNull(secondResponse);
        Assert.Equal(firstResponse.LockId, secondResponse.LockId);
        Assert.Equal(firstResponse.ExpiresAt, secondResponse.ExpiresAt);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.FileLocks.Where(l => l.DeliveryId == delivery).ToListAsync();
            Assert.Single(rows);
        }
    }

    [Fact]
    public async Task Lock_InvalidDuration_Returns400()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        foreach (var duration in new int?[] { 0, -5, FileLockLimits.MaxDurationMinutes + 1 })
        {
            var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock",
                new LockDeliveryRequest { DurationMinutes = duration });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(0, await db.FileLocks.CountAsync(l => l.DeliveryId == delivery));
        }
    }

    [Fact]
    public async Task Lock_NotOwner_Returns404()
    {
        var otherUserId = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(otherUserId);

        Guid deliveryId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var delivery = new Delivery
            {
                Id = Guid.CreateVersion7(),
                FileName = TestFileName,
                ContentType = TestContentType,
                FileSize = TestFileSize,
                TotalBytes = TestFileSize,
                ChunkCount = 1,
                ShareCode = "flk" + Guid.NewGuid().ToString("N")[..8],
                Status = DeliveryStatus.Active,
                SenderId = Guid.Parse(otherUserId),
                MaxDownloads = 5,
                ExpiresAt = DateTime.UtcNow.AddDays(7),
            };
            db.Deliveries.Add(delivery);
            await db.SaveChangesAsync();
            deliveryId = delivery.Id;
        }

        var client = _factory.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/deliveries/{deliveryId}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Lock_ForceBreaksForeignLock_NotifiesHolder()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var holderId = Guid.NewGuid();
        await _factory.SeedUserAsync(holderId.ToString());
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FileLocks.Add(new FileLock
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery,
                LockedById = holderId,
                Reason = "holding",
                ExpiresAt = DateTime.UtcNow.AddHours(2),
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lockResponse = await response.Content.ReadFromJsonAsync<LockDeliveryResponse>();
        Assert.NotNull(lockResponse);
        Assert.Equal(_factory.TestUserId, lockResponse.LockedById);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var rows = await db.FileLocks.Where(l => l.DeliveryId == delivery).OrderBy(l => l.CreatedAt).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.NotNull(rows[0].ReleasedAt);
            Assert.Equal(holderId, rows[0].LockedById);
            Assert.Null(rows[1].ReleasedAt);
            Assert.Equal(_factory.TestUserId, rows[1].LockedById);

            var notification = await db.Notifications
                .SingleAsync(n => n.UserId == holderId && n.Type == NotificationType.FileLockBroken);
            Assert.Equal(TestFileName, notification.Title);
            Assert.Equal($"{RoutePaths.AccountDeliveries}/{delivery}", notification.LinkUrl);

            var securityEvent = await db.SecurityEvents
                .SingleAsync(e => e.UserId == _factory.TestUserId && e.EventType == SecurityEventType.FileLockForceBroken);
            Assert.Contains(delivery.ToString(), securityEvent.MetadataJson);
        }
    }

    [Fact]
    public async Task Unlock_ReleasesActiveLock()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var lockResponse = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var unlockResponse = await client.PostAsync($"/api/deliveries/{delivery}/unlock", null);
        Assert.Equal(HttpStatusCode.NoContent, unlockResponse.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.FileLocks.SingleAsync(l => l.DeliveryId == delivery);
            Assert.NotNull(row.ReleasedAt);
        }
    }

    [Fact]
    public async Task Unlock_NoActiveLock_NoOp()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var response = await client.PostAsync($"/api/deliveries/{delivery}/unlock", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Unlock_ReleasingForeignLock_NotifiesHolder()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var holderId = Guid.NewGuid();
        await _factory.SeedUserAsync(holderId.ToString());
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FileLocks.Add(new FileLock
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery,
                LockedById = holderId,
                ExpiresAt = DateTime.UtcNow.AddHours(2),
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsync($"/api/deliveries/{delivery}/unlock", null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.FileLocks.SingleAsync(l => l.DeliveryId == delivery);
            Assert.NotNull(row.ReleasedAt);

            var notification = await db.Notifications
                .SingleAsync(n => n.UserId == holderId && n.Type == NotificationType.FileLockBroken);
            Assert.Equal(TestFileName, notification.Title);
        }
    }

    [Fact]
    public async Task ReplaceStart_ForeignLock_Blocked423()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var holderId = Guid.NewGuid();
        await _factory.SeedUserAsync(holderId.ToString());
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FileLocks.Add(new FileLock
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = delivery,
                LockedById = holderId,
                ExpiresAt = DateTime.UtcNow.AddHours(2),
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/replace/start",
            new ReplaceDeliveryStartRequest
            {
                FileName = "v2.bin",
                FileSize = TestFileSize,
                ContentType = TestContentType,
                PartNumbers = [1],
            });
        Assert.Equal((HttpStatusCode)423, response.StatusCode);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var lockRow = await db.FileLocks.SingleAsync(l => l.DeliveryId == delivery);
            Assert.Null(lockRow.ReleasedAt);
            Assert.Equal(holderId, lockRow.LockedById);
        }
    }

    [Fact]
    public async Task ReplaceStart_SelfLock_Allowed()
    {
        var client = _factory.CreateAuthenticatedClient();
        var delivery = await UploadFileAsync(client);

        var lockResponse = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var response = await client.PostAsJsonAsync($"/api/deliveries/{delivery}/replace/start",
            new ReplaceDeliveryStartRequest
            {
                FileName = "v2.bin",
                FileSize = TestFileSize,
                ContentType = TestContentType,
                PartNumbers = [1],
            });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task List_IncludesActiveLock()
    {
        var client = _factory.CreateAuthenticatedClient();
        var lockedDelivery = await UploadFileAsync(client);
        var unlockedDelivery = await UploadFileAsync(client);

        var lockResponse = await client.PostAsJsonAsync($"/api/deliveries/{lockedDelivery}/lock", new LockDeliveryRequest());
        Assert.Equal(HttpStatusCode.OK, lockResponse.StatusCode);

        var list = await client.GetFromJsonAsync<DeliveryListPayload>("/api/deliveries");
        Assert.NotNull(list);
        var lockedItem = Assert.Single(list.Items, i => i.Id == lockedDelivery);
        Assert.NotNull(lockedItem.Lock);
        Assert.True(lockedItem.Lock.LockedBySelf);
        Assert.Equal(_factory.TestUserId, lockedItem.Lock.LockedById);

        var unlockedItem = Assert.Single(list.Items, i => i.Id == unlockedDelivery);
        Assert.Null(unlockedItem.Lock);
    }

    [Fact]
    public async Task ExpiryJob_ReleasesOnlyExpiredLocks()
    {
        var client = _factory.CreateAuthenticatedClient();
        var expiredDelivery = await UploadFileAsync(client);
        var activeDelivery = await UploadFileAsync(client);

        var expiredLockId = Guid.CreateVersion7();
        var activeLockId = Guid.CreateVersion7();
        await using (var seedScope = _factory.Services.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FileLocks.AddRange(
                new FileLock
                {
                    Id = expiredLockId,
                    DeliveryId = expiredDelivery,
                    LockedById = _factory.TestUserId,
                    ExpiresAt = DateTime.UtcNow.AddMinutes(-5),
                },
                new FileLock
                {
                    Id = activeLockId,
                    DeliveryId = activeDelivery,
                    LockedById = _factory.TestUserId,
                    ExpiresAt = DateTime.UtcNow.AddHours(2),
                });
            await db.SaveChangesAsync();
        }

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        var job = new FileLockExpiryJob(scopeFactory, NullLogger<FileLockExpiryJob>.Instance);
        await job.ReleaseExpiredLocksAsync();

        await using (var verifyScope = scopeFactory.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var expired = await db.FileLocks.AsNoTracking().SingleAsync(l => l.Id == expiredLockId);
            var active = await db.FileLocks.AsNoTracking().SingleAsync(l => l.Id == activeLockId);
            Assert.NotNull(expired.ReleasedAt);
            Assert.Null(active.ReleasedAt);
        }
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
        var commit = await commitResponse.Content.ReadFromJsonAsync<CommitR2Response>();
        Assert.NotNull(commit);
        return start.DeliveryId;
    }

    private sealed class DeliveryListPayload
    {
        public List<DeliveryItem> Items { get; set; } = [];
        public string? NextCursor { get; set; }
    }
}