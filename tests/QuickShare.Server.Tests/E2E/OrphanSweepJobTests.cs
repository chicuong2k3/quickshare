using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Infrastructure;
using QuickShare.Infrastructure.Jobs;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// OrphanSweepJob (issue #107): only unreferenced, old-enough R2 data is reclaimed,
/// and only when delete mode is on. Uses the real test DB + a recording storage fake.
/// </summary>
public sealed class OrphanSweepJobTests : IClassFixture<TestWebAppFactory>
{
    private readonly TestWebAppFactory _factory;

    public OrphanSweepJobTests(TestWebAppFactory factory) => _factory = factory;

    private const string OrphanKey = "uploads/orphan/aaaa/file.bin";
    private const string LinkedKey = "uploads/linked/bbbb/file.bin";
    private const string SessionKey = "uploads/session/cccc/file.bin";
    private const string OrphanUploadId = "orphan-upload-id";
    private const string TrackedUploadId = "tracked-upload-id";

    private async Task<(OrphanSweepJob Job, RecordingStorage Storage)> ArrangeAsync(bool deleteMode, int maxDeletes = 500)
    {
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Deliveries.AnyAsync(d => d.StoragePath == LinkedKey))
            {
                db.Deliveries.Add(new Delivery
                {
                    FileName = "linked.bin",
                    ShareCode = "orphlk",
                    StoragePath = LinkedKey,
                    Status = DeliveryStatus.Active,
                });
                db.UploadSessions.Add(new UploadSession
                {
                    FileName = "session.bin",
                    ExpiresAt = DateTime.UtcNow.AddHours(1),
                    R2UploadId = TrackedUploadId,
                    StoragePath = SessionKey,
                });
                await db.SaveChangesAsync();
            }
        }

        var storage = new RecordingStorage
        {
            // Fresh orphans are filtered by the provider's olderThan listing — the fake
            // returns only what a real provider would: stale keys.
            StaleObjects = [OrphanKey, LinkedKey, SessionKey],
            StaleUploads =
            [
                new R2MultipartInfo(OrphanUploadId, OrphanKey, DateTime.UtcNow.AddDays(-2)),
                new R2MultipartInfo(TrackedUploadId, SessionKey, DateTime.UtcNow.AddDays(-2)),
            ],
        };

        var job = new OrphanSweepJob(
            scopeFactory,
            storage,
            Options.Create(new QuickShareOptions
            {
                OrphanSweepDelete = deleteMode,
                OrphanSweepMaxDeletesPerCycle = maxDeletes,
            }),
            NullLogger<OrphanSweepJob>.Instance);

        return (job, storage);
    }

    [Fact]
    public async Task DryRun_DeletesNothing_ButAbortsUntrackedMultipart()
    {
        var (job, storage) = await ArrangeAsync(deleteMode: false);

        await job.SweepAsync();

        Assert.Empty(storage.DeletedKeys);
        Assert.Equal([OrphanUploadId], storage.AbortedUploadIds);
    }

    [Fact]
    public async Task DeleteMode_DeletesOnlyOrphans()
    {
        var (job, storage) = await ArrangeAsync(deleteMode: true);

        await job.SweepAsync();

        Assert.Equal([OrphanKey], storage.DeletedKeys);
        Assert.DoesNotContain(LinkedKey, storage.DeletedKeys);
        Assert.DoesNotContain(SessionKey, storage.DeletedKeys);
        Assert.Equal([OrphanUploadId], storage.AbortedUploadIds);
        Assert.DoesNotContain(TrackedUploadId, storage.AbortedUploadIds);
    }

    [Fact]
    public async Task Sweep_StaysInsideUploadsPrefix_AndUsesAgeGuards()
    {
        var (job, storage) = await ArrangeAsync(deleteMode: false);
        var before = DateTime.UtcNow;

        await job.SweepAsync();

        Assert.Equal(R2ObjectKey.UploadsPrefix, storage.ListedObjectPrefix);
        Assert.Equal(R2ObjectKey.UploadsPrefix, storage.ListedUploadPrefix);
        // Default guards: objects >7d, multiparts >24h — cutoffs must sit in the past
        // at roughly those distances (1-minute tolerance for test runtime).
        Assert.InRange(storage.ListedObjectOlderThan!.Value,
            before.AddDays(-7).AddMinutes(-1), before.AddDays(-7).AddMinutes(1));
        Assert.InRange(storage.ListedUploadOlderThan!.Value,
            before.AddHours(-24).AddMinutes(-1), before.AddHours(-24).AddMinutes(1));
    }

    [Fact]
    public async Task DeleteMode_CandidateSpike_DowngradesToDryRun()
    {
        var (job, storage) = await ArrangeAsync(deleteMode: true, maxDeletes: 0);

        await job.SweepAsync();

        Assert.Empty(storage.DeletedKeys);
    }

    private sealed class RecordingStorage : IStorageProvider
    {
        public IReadOnlyList<string> StaleObjects { get; init; } = [];
        public IReadOnlyList<R2MultipartInfo> StaleUploads { get; init; } = [];
        public List<string> DeletedKeys { get; } = [];
        public List<string> AbortedUploadIds { get; } = [];
        public string? ListedObjectPrefix { get; private set; }
        public DateTime? ListedObjectOlderThan { get; private set; }
        public string? ListedUploadPrefix { get; private set; }
        public DateTime? ListedUploadOlderThan { get; private set; }

        public Task<IReadOnlyList<string>> ListObjectsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        {
            ListedObjectPrefix = prefix;
            ListedObjectOlderThan = olderThan;
            return Task.FromResult(StaleObjects);
        }

        public Task<IReadOnlyList<R2MultipartInfo>> ListMultipartUploadsAsync(string prefix, DateTime olderThan, CancellationToken ct = default)
        {
            ListedUploadPrefix = prefix;
            ListedUploadOlderThan = olderThan;
            return Task.FromResult(StaleUploads);
        }

        public Task DeleteObjectAsync(string objectKey, CancellationToken ct = default)
        {
            DeletedKeys.Add(objectKey);
            return Task.CompletedTask;
        }

        public Task DeleteObjectsAsync(string prefix, CancellationToken ct = default)
            => throw new NotSupportedException("Sweep must use exact-key DeleteObjectAsync, never prefix delete.");

        public Task CopyObjectAsync(string sourceKey, string destinationKey, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task AbortR2MultipartUploadAsync(string uploadId, string objectKey, CancellationToken ct = default)
        {
            AbortedUploadIds.Add(uploadId);
            return Task.CompletedTask;
        }

        public Task<R2InitResult> InitR2MultipartUploadAsync(Guid deliveryId, string fileName, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<R2PartUrl>> GenerateUploadPartUrlsAsync(string uploadId, string objectKey, IReadOnlyList<int> partNumbers, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CompletionResult> CompleteR2MultipartUploadAsync(string uploadId, string objectKey, IReadOnlyList<R2PartEtag> parts, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<long> GetObjectSizeAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult(-1L);

        public Task UploadObjectAsync(string objectKey, Stream content, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Uri> GetPresignedDownloadUrlAsync(string objectKey, TimeSpan expiry, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string objectKey, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);
    }
}
