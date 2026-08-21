using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Hangfire;

namespace QuickShare.Infrastructure.Jobs;

/// <summary>
/// Exports each due workspace to storage the customer controls (issue #271).
///
/// The manifest is metadata, not bytes: workspace, seats, folders and one record per delivery
/// including its storage key, size and checksum. Copying the objects themselves on a schedule
/// would be a bandwidth bill nobody agreed to; the keys are what a restore actually needs, and
/// the customer can pull the objects on their own terms.
///
/// Every run is checksummed and audited. A failed run records why and does not prune — pruning
/// old copies on the strength of a run that did not produce a new one is how a retention policy
/// deletes the last good backup.
/// </summary>
public sealed class WorkspaceBackupJob(
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceBackupJob> logger)
{
    private const string ManifestContentType = "application/json";
    /// <summary>Manifest format version. Bump when the shape changes so a restore can branch on it.</summary>
    private const int ManifestVersion = 1;

    [DisableConcurrentExecution(timeoutInSeconds: 0)]
    [AutomaticRetry(Attempts = 0)]
    public async Task RunAsync()
    {
        var ct = CancellationToken.None;
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var policies = await db.WorkspaceBackupPolicies
            .Include(p => p.Workspace)
            .Where(p => p.Enabled)
            .ToListAsync(ct);

        foreach (var policy in policies.Where(p => p.IsDue(now)))
        {
            await RunOneAsync(db, policy, ct);
        }
    }

    /// <summary>
    /// Runs one policy and records the outcome. Shared with the run-now endpoint so a manual
    /// run and a scheduled one cannot diverge — a "test backup" that exercises a different code
    /// path proves nothing about the scheduled one.
    /// </summary>
    public async Task RunOneAsync(AppDbContext db, WorkspaceBackupPolicy policy, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        policy.LastRunAt = now;
        policy.UpdatedAt = now;

        try
        {
            var manifest = await BuildManifestAsync(db, policy.WorkspaceId, now, ct);
            var body = JsonSerializer.SerializeToUtf8Bytes(manifest);
            var key = ManifestKey(policy, now);

            using var client = CreateClient(policy);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = policy.BucketName,
                Key = key,
                ContentType = ManifestContentType,
                InputStream = new MemoryStream(body),
                // Checksum travels with the object so a restore can prove the copy is intact
                // without trusting our own record of it.
                ChecksumSHA256 = Convert.ToBase64String(SHA256.HashData(body)),
            }, ct);

            policy.LastStatus = BackupRunStatus.Succeeded;
            policy.LastError = null;
            policy.LastManifestKey = key;
            policy.LastManifestBytes = body.Length;
            policy.LastDeliveryCount = manifest.Deliveries.Count;

            await PruneAsync(client, policy, ct);
        }
        catch (Exception ex)
        {
            // Never let one misconfigured destination stop the others; the error is the report.
            logger.LogWarning(ex, "Workspace backup failed for {WorkspaceId}", policy.WorkspaceId);
            policy.LastStatus = BackupRunStatus.Failed;
            policy.LastError = Truncate(ex.Message);
        }

        // Audited inline rather than through WorkspaceService: a background run has no
        // HttpContext, and the actor is the workspace owner on whose behalf it ran.
        var ownerId = policy.Workspace?.OwnerId
            ?? await db.Workspaces.Where(w => w.Id == policy.WorkspaceId).Select(w => w.OwnerId).FirstAsync(ct);

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = ownerId,
            WorkspaceId = policy.WorkspaceId,
            EventType = SecurityEventType.WorkspaceBackupRun,
            IpHash = string.Empty,
            UserAgentLabel = nameof(WorkspaceBackupJob),
            MetadataJson = JsonSerializer.Serialize(
                new { policy.LastStatus, policy.LastManifestKey, policy.LastDeliveryCount }),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Keeps the newest N manifests and deletes the rest. Only ever reached after a successful
    /// upload: pruning on the strength of a run that produced nothing is how a retention policy
    /// deletes the last good backup.
    /// </summary>
    private static async Task PruneAsync(AmazonS3Client client, WorkspaceBackupPolicy policy, CancellationToken ct)
    {
        var listed = await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = policy.BucketName,
            Prefix = KeyPrefix(policy),
        }, ct);

        var stale = listed.S3Objects
            .OrderByDescending(o => o.LastModified)
            .Skip(policy.RetainedCopies)
            .ToList();

        foreach (var obj in stale)
        {
            await client.DeleteObjectAsync(policy.BucketName, obj.Key, ct);
        }
    }

    private static async Task<BackupManifest> BuildManifestAsync(
        AppDbContext db, Guid workspaceId, DateTime now, CancellationToken ct)
    {
        var workspace = await db.Workspaces
            .AsNoTracking()
            .Include(w => w.Owner)
            .FirstAsync(w => w.Id == workspaceId, ct);

        var members = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.RevokedAt == null)
            .Select(m => new BackupMember(m.InviteEmail, m.Role.ToString().ToLowerInvariant(), m.JoinedAt))
            .ToListAsync(ct);

        var folders = await db.WorkspaceFolders
            .AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId)
            .Select(f => new BackupFolder(f.Id, f.ParentId, f.Name))
            .ToListAsync(ct);

        // Live rows only — a backup of the trash is not what anybody means by a backup.
        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId && d.DeletedAt == null)
            .Select(d => new BackupDelivery(
                d.Id,
                d.FolderId,
                d.FileName,
                d.ContentType,
                d.FileSize,
                d.ShareCode,
                d.StoragePath,
                d.Sha256,
                d.CreatedAt,
                d.ExpiresAt))
            .ToListAsync(ct);

        return new BackupManifest(
            ManifestVersion,
            now,
            workspace.Id,
            workspace.Name,
            workspace.Owner.Email,
            members,
            folders,
            deliveries);
    }

    /// <summary>Bounded timeout: a destination that hangs must not stall the hourly sweep.</summary>
    private static readonly TimeSpan DestinationTimeout = TimeSpan.FromSeconds(30);

    private static AmazonS3Client CreateClient(WorkspaceBackupPolicy policy) =>
        new(policy.AccessKeyId, policy.AccessKeySecret, new AmazonS3Config
        {
            ServiceURL = policy.Endpoint,
            Timeout = DestinationTimeout,
            MaxErrorRetry = 1,
            ForcePathStyle = true,
            AuthenticationRegion = policy.Region,
        });

    private static string KeyPrefix(WorkspaceBackupPolicy policy) =>
        string.IsNullOrEmpty(policy.KeyPrefix)
            ? $"quickshare/{policy.WorkspaceId:N}/"
            : $"{policy.KeyPrefix.TrimEnd('/')}/{policy.WorkspaceId:N}/";

    /// <summary>Sortable timestamp in the key, so listing by name and by date agree.</summary>
    private static string ManifestKey(WorkspaceBackupPolicy policy, DateTime now) =>
        $"{KeyPrefix(policy)}{now:yyyyMMddTHHmmssZ}-manifest.json";

    private static string Truncate(string value) =>
        value.Length > WorkspaceBackupPolicy.ErrorMaxLength
            ? value[..WorkspaceBackupPolicy.ErrorMaxLength]
            : value;
}

// ── Manifest shape (version 1) ──

public sealed record BackupMember(string Email, string Role, DateTime? JoinedAt);

public sealed record BackupFolder(Guid Id, Guid? ParentId, string Name);

/// <summary>
/// One delivery record. <c>StorageKey</c> is the point of the whole manifest: it is what lets
/// the customer pull the bytes from object storage themselves.
/// </summary>
public sealed record BackupDelivery(
    Guid Id,
    Guid? FolderId,
    string FileName,
    string ContentType,
    long FileSize,
    string ShareCode,
    string? StorageKey,
    string? Sha256,
    DateTime CreatedAt,
    DateTime? ExpiresAt);

public sealed record BackupManifest(
    int Version,
    DateTime GeneratedAt,
    Guid WorkspaceId,
    string WorkspaceName,
    string OwnerEmail,
    IReadOnlyList<BackupMember> Members,
    IReadOnlyList<BackupFolder> Folders,
    IReadOnlyList<BackupDelivery> Deliveries);
