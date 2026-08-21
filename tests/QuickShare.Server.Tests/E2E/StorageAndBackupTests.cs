using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuickShare.Data;
using QuickShare.Data.Entities;
using QuickShare.Features.Workspaces.Backup;
using QuickShare.Features.Workspaces.Manage;
using QuickShare.Infrastructure.Constants;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// S3-compatible object storage (#171) and scheduled workspace backup (#271).
///
/// The storage cases pin *configuration resolution* rather than round-tripping bytes: the
/// provider is the AWS SDK either way, and what actually changed in #171 is which endpoint,
/// bucket and addressing style it is handed — plus the promise that an existing deployment
/// configured under the old `R2` section keeps working after the upgrade. Contract tests
/// against a live MinIO belong in CI with a container, not in an in-memory suite.
///
/// The backup cases pin validation and the due-schedule arithmetic. Shipping to a real bucket
/// is exercised by the run-now endpoint against an unreachable endpoint, which is the failure
/// path that matters: it has to be recorded, not thrown.
/// </summary>
public sealed class StorageAndBackupTests : IClassFixture<TestWebAppFactory>
{
    private const string MinioEndpoint = "https://minio.internal:9000";
    private const string Bucket = "quickshare-backups";

    private readonly TestWebAppFactory _factory;

    public StorageAndBackupTests(TestWebAppFactory factory) => _factory = factory;

    // ── Object store configuration (#171) ──

    [Fact]
    public void ObjectStore_DerivesTheR2EndpointWhenOnlyAnAccountIdIsGiven()
    {
        var options = new ObjectStoreOptions { AccountId = "abc123" };

        Assert.Equal("https://abc123.r2.cloudflarestorage.com", options.ResolvedEndpoint);
    }

    [Fact]
    public void ObjectStore_PrefersAnExplicitEndpointOverTheDerivedOne()
    {
        var options = new ObjectStoreOptions { AccountId = "abc123", Endpoint = MinioEndpoint };

        // Explicit config wins, which is the whole of #171: a self-hoster points at MinIO.
        Assert.Equal(MinioEndpoint, options.ResolvedEndpoint);
    }

    [Fact]
    public void ObjectStore_IsNotConfiguredWithoutCredentialsOrBucket()
    {
        Assert.False(new ObjectStoreOptions().IsConfigured);
        Assert.False(new ObjectStoreOptions { Endpoint = MinioEndpoint }.IsConfigured);
        Assert.False(new ObjectStoreOptions
        {
            Endpoint = MinioEndpoint,
            AccessKeyId = "key",
            AccessKeySecret = "secret",
        }.IsConfigured);

        Assert.True(new ObjectStoreOptions
        {
            Endpoint = MinioEndpoint,
            AccessKeyId = "key",
            AccessKeySecret = "secret",
            BucketName = Bucket,
        }.IsConfigured);
    }

    [Fact]
    public void ObjectStore_FallsBackToTheLegacyR2SectionSoUpgradesDoNotBreak()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["R2:AccountId"] = "legacy-account",
                ["R2:BucketName"] = "legacy-bucket",
                ["R2:AccessKeyId"] = "legacy-key",
                ["R2:AccessKeySecret"] = "legacy-secret",
                ["R2:PublicBaseUrl"] = "https://cdn.example.com",
            })
            .Build();

        var resolved = ObjectStoreResolver.Resolve(configuration);

        Assert.True(resolved.IsConfigured);
        Assert.Equal("https://legacy-account.r2.cloudflarestorage.com", resolved.ResolvedEndpoint);
        Assert.Equal("legacy-bucket", resolved.BucketName);
        Assert.Equal("https://cdn.example.com", resolved.PublicBaseUrl);
    }

    [Fact]
    public void ObjectStore_CurrentSectionWinsOverTheLegacyOne()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["R2:AccountId"] = "legacy-account",
                ["R2:BucketName"] = "legacy-bucket",
                ["R2:AccessKeyId"] = "legacy-key",
                ["R2:AccessKeySecret"] = "legacy-secret",
                ["ObjectStore:Endpoint"] = MinioEndpoint,
                ["ObjectStore:BucketName"] = "minio-bucket",
                ["ObjectStore:AccessKeyId"] = "minio-key",
                ["ObjectStore:AccessKeySecret"] = "minio-secret",
                ["ObjectStore:ForcePathStyle"] = "false",
            })
            .Build();

        var resolved = ObjectStoreResolver.Resolve(configuration);

        Assert.Equal(MinioEndpoint, resolved.ResolvedEndpoint);
        Assert.Equal("minio-bucket", resolved.BucketName);
        Assert.False(resolved.ForcePathStyle);
    }

    // ── Backup policy validation (#271) ──

    [Fact]
    public async Task Backup_RefusesAPlaintextDestination()
    {
        var (client, _) = await WorkspaceClientAsync();

        var response = await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
        {
            Endpoint = "http://minio.internal:9000",
            BucketName = Bucket,
        });

        // Credentials and a workspace inventory crossing the network in the clear.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Backup_RequiresABucketAndAKnownFrequency()
    {
        var (client, _) = await WorkspaceClientAsync();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
            {
                Endpoint = MinioEndpoint,
                BucketName = "   ",
            })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
            {
                Endpoint = MinioEndpoint,
                BucketName = Bucket,
                Frequency = "hourly",
            })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
            {
                Endpoint = MinioEndpoint,
                BucketName = Bucket,
                Frequency = BackupFrequency.Daily,
                RetainedCopies = 0,
            })).StatusCode);
    }

    [Fact]
    public async Task Backup_SavesAndKeepsTheSecretWriteOnly()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        var save = await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
        {
            Endpoint = MinioEndpoint,
            BucketName = Bucket,
            Frequency = BackupFrequency.Daily,
            RetainedCopies = 7,
            AccessKeyId = "AKIA-test",
            AccessKeySecret = "destination-secret",
        });
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);

        var body = await save.Content.ReadAsStringAsync();
        Assert.DoesNotContain("destination-secret", body, StringComparison.Ordinal);

        var read = await client.GetFromJsonAsync<BackupPolicyDto>("/api/workspace/backup");
        Assert.NotNull(read);
        Assert.True(read.HasSecret);
        Assert.Equal(BackupRunStatus.Never, read.LastStatus);

        // Saving again without a secret keeps the stored one — the field is never populated
        // back into the form, so a blank submit is the normal case.
        await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
        {
            Endpoint = MinioEndpoint,
            BucketName = Bucket,
            Frequency = BackupFrequency.Weekly,
            RetainedCopies = 7,
            AccessKeyId = "AKIA-test",
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy = await db.WorkspaceBackupPolicies.AsNoTracking()
            .SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal("destination-secret", policy.AccessKeySecret);
        Assert.Equal(BackupFrequency.Weekly, policy.Frequency);
    }

    [Fact]
    public async Task Backup_IsAdminOnly()
    {
        var (_, _) = await WorkspaceClientAsync();
        var (outsider, _) = await WorkspaceClientAsync();
        Assert.NotNull(outsider);

        // A second workspace owner is an admin of their own workspace, so probe with an
        // account that holds no seat at all.
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var seatless = _factory.CreateAuthenticatedClient(providerKey);

        Assert.Equal(HttpStatusCode.Forbidden, (await seatless.GetAsync("/api/workspace/backup")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await seatless.PostAsync("/api/workspace/backup/run", null)).StatusCode);
    }

    // ── Schedule arithmetic (#271) ──

    [Fact]
    public void Backup_IsDueOnlyWhenConfiguredEnabledAndTheIntervalHasElapsed()
    {
        var now = new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

        var configured = new WorkspaceBackupPolicy
        {
            Enabled = true,
            Endpoint = MinioEndpoint,
            BucketName = Bucket,
            Frequency = BackupFrequency.Daily,
        };

        // Never run: due immediately.
        Assert.True(configured.IsDue(now));

        configured.LastRunAt = now.AddHours(-23);
        Assert.False(configured.IsDue(now));

        configured.LastRunAt = now.AddHours(-25);
        Assert.True(configured.IsDue(now));

        configured.Frequency = BackupFrequency.Weekly;
        Assert.False(configured.IsDue(now));
        configured.LastRunAt = now.AddDays(-8);
        Assert.True(configured.IsDue(now));

        // Disabled or half-configured is never due, however long it has been.
        configured.Enabled = false;
        Assert.False(configured.IsDue(now));

        Assert.False(new WorkspaceBackupPolicy { Enabled = true, BucketName = Bucket }.IsDue(now));
        Assert.False(new WorkspaceBackupPolicy { Enabled = true, Endpoint = MinioEndpoint }.IsDue(now));
    }

    [Fact]
    public async Task Backup_RunAgainstAnUnreachableDestinationRecordsTheFailure()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();

        await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
        {
            // Reserved TEST-NET-1 address: guaranteed not to resolve to anybody's bucket.
            Endpoint = "https://192.0.2.1:9000",
            BucketName = Bucket,
            Frequency = BackupFrequency.Daily,
            AccessKeyId = "key",
            AccessKeySecret = "secret",
        });

        var run = await client.PostAsync("/api/workspace/backup/run", null);

        // 200 with the failure in the body: the request was fine, the destination was not, and
        // that distinction is what the admin asked the endpoint to tell them.
        Assert.Equal(HttpStatusCode.OK, run.StatusCode);
        var result = await run.Content.ReadFromJsonAsync<BackupPolicyDto>();
        Assert.NotNull(result);
        Assert.Equal(BackupRunStatus.Failed, result.LastStatus);
        Assert.False(string.IsNullOrEmpty(result.LastError));
        Assert.NotNull(result.LastRunAt);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Every run is on the workspace timeline, successful or not.
        Assert.Contains(await db.SecurityEvents.AsNoTracking().ToListAsync(),
            e => e.WorkspaceId == workspaceId && e.EventType == SecurityEventType.WorkspaceBackupRun);
    }

    [Fact]
    public async Task Backup_RunWithoutAPolicyIs404()
    {
        var (client, _) = await WorkspaceClientAsync();

        var run = await client.PostAsync("/api/workspace/backup/run", null);

        Assert.Equal(HttpStatusCode.NotFound, run.StatusCode);
    }

    [Fact]
    public async Task Backup_DeleteForgetsTheDestination()
    {
        var (client, workspaceId) = await WorkspaceClientAsync();
        await client.PutAsJsonAsync("/api/workspace/backup", new BackupPolicyRequest
        {
            Endpoint = MinioEndpoint,
            BucketName = Bucket,
            AccessKeySecret = "secret",
        });

        var deleted = await client.DeleteAsync("/api/workspace/backup");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.WorkspaceBackupPolicies.AsNoTracking().AnyAsync(p => p.WorkspaceId == workspaceId));
    }

    // ── Helpers ──

    private async Task<(HttpClient Client, Guid WorkspaceId)> WorkspaceClientAsync()
    {
        var providerKey = Guid.NewGuid().ToString();
        await _factory.SeedUserAsync(providerKey, PlanTierLimits.Studio);
        var client = _factory.CreateAuthenticatedClient(providerKey);

        var response = await client.PostAsJsonAsync("/api/workspace",
            new CreateWorkspaceRequest { Name = $"Backup {Guid.NewGuid():N}"[..20], Seats = 3 });
        response.EnsureSuccessStatusCode();
        var workspace = (await response.Content.ReadFromJsonAsync<WorkspaceDto>())!;

        return (client, workspace.Id);
    }
}
