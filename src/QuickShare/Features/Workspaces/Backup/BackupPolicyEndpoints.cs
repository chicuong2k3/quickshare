using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Jobs;

namespace QuickShare.Features.Workspaces.Backup;

public sealed record BackupPolicyDto(
    bool Enabled,
    string Frequency,
    int RetainedCopies,
    string Endpoint,
    string Region,
    string BucketName,
    string KeyPrefix,
    string AccessKeyId,
    bool HasSecret,
    DateTime? LastRunAt,
    string LastStatus,
    string? LastError,
    string? LastManifestKey,
    long LastManifestBytes,
    int LastDeliveryCount);

public sealed class BackupPolicyRequest
{
    public bool Enabled { get; set; } = true;
    public string? Frequency { get; set; }
    public int RetainedCopies { get; set; } = 4;
    public string? Endpoint { get; set; }
    public string? Region { get; set; }
    public string? BucketName { get; set; }
    public string? KeyPrefix { get; set; }
    public string? AccessKeyId { get; set; }
    /// <summary>Blank leaves the stored secret alone; a value replaces it.</summary>
    public string? AccessKeySecret { get; set; }
}

/// <summary>GET /api/workspace/backup — the scheduled export policy and its last run (issue #271).</summary>
public sealed class GetBackupPolicyEndpoint : EndpointWithoutRequest<BackupPolicyDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/backup");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace's backup policy";
            s.Description = "Workspace admins only. Includes the outcome of the last run: status, error, manifest key and delivery count.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var policy = await db.WorkspaceBackupPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(policy is null ? null : Map(policy), ct);
    }

    /// <summary>The destination secret is never returned, only whether one is set.</summary>
    internal static BackupPolicyDto Map(WorkspaceBackupPolicy policy) => new(
        policy.Enabled,
        policy.Frequency,
        policy.RetainedCopies,
        policy.Endpoint,
        policy.Region,
        policy.BucketName,
        policy.KeyPrefix,
        policy.AccessKeyId,
        !string.IsNullOrEmpty(policy.AccessKeySecret),
        policy.LastRunAt,
        policy.LastStatus,
        policy.LastError,
        policy.LastManifestKey,
        policy.LastManifestBytes,
        policy.LastDeliveryCount);
}

/// <summary>
/// PUT /api/workspace/backup — schedule an export to the customer's own bucket (issue #271).
///
/// HTTPS only, for the same reason the SIEM sink is: these are credentials and a workspace
/// inventory crossing the network. The destination is deliberately *not* validated by writing a
/// probe object — a settings save that silently creates objects in someone's bucket is a
/// surprise, and the run-now endpoint exists precisely so the check is something the admin asks
/// for on purpose.
/// </summary>
public sealed class UpdateBackupPolicyEndpoint : Endpoint<BackupPolicyRequest, BackupPolicyDto>
{
    public override void Configure()
    {
        Put("/api/workspace/backup");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Configure scheduled workspace backup";
            s.Description = "Workspace admins only. Exports a metadata manifest (workspace, seats, folders, delivery records with their storage keys) to an S3-compatible bucket you control. File bytes are not copied.";
        });
    }

    public override async Task HandleAsync(BackupPolicyRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var endpoint = (req.Endpoint ?? string.Empty).Trim();
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _)
            || !endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || endpoint.Length > WorkspaceBackupPolicy.EndpointMaxLength)
        {
            ThrowError(ErrorCodes.BackupEndpointInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var bucket = (req.BucketName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(bucket) || bucket.Length > WorkspaceBackupPolicy.BucketMaxLength)
        {
            ThrowError(ErrorCodes.BackupBucketRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var frequency = (req.Frequency ?? BackupFrequency.Weekly).Trim().ToLowerInvariant();
        if (!BackupFrequency.All.Contains(frequency))
        {
            ThrowError(ErrorCodes.BackupFrequencyInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (req.RetainedCopies < WorkspaceBackupPolicy.MinRetainedCopies
            || req.RetainedCopies > WorkspaceBackupPolicy.MaxRetainedCopies)
        {
            ThrowError(ErrorCodes.BackupRetentionInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var policy = await db.WorkspaceBackupPolicies
            .FirstOrDefaultAsync(p => p.WorkspaceId == seat.WorkspaceId, ct);

        if (policy is null)
        {
            policy = new WorkspaceBackupPolicy
            {
                Id = Guid.CreateVersion7(),
                WorkspaceId = seat.WorkspaceId,
                CreatedAt = DateTime.UtcNow,
            };
            db.WorkspaceBackupPolicies.Add(policy);
        }

        policy.Enabled = req.Enabled;
        policy.Frequency = frequency;
        policy.RetainedCopies = req.RetainedCopies;
        policy.Endpoint = endpoint;
        policy.Region = Trim(req.Region, 64, ObjectStoreDefaults.Region);
        policy.BucketName = bucket;
        policy.KeyPrefix = Trim(req.KeyPrefix, WorkspaceBackupPolicy.PrefixMaxLength, string.Empty);
        policy.AccessKeyId = Trim(req.AccessKeyId, WorkspaceBackupPolicy.SecretMaxLength, string.Empty);
        policy.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(req.AccessKeySecret))
            policy.AccessKeySecret = Trim(req.AccessKeySecret, WorkspaceBackupPolicy.SecretMaxLength, string.Empty);

        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceBackupPolicyChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { frequency, bucket, policy.RetainedCopies, policy.Enabled }));

        await Send.OkAsync(GetBackupPolicyEndpoint.Map(policy), ct);
    }

    private static string Trim(string? value, int max, string fallback)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }
}

/// <summary>
/// POST /api/workspace/backup/run — run the export now (issue #271).
///
/// Goes through the same code path as the scheduled run, deliberately: a "test backup" that
/// exercises a different path proves nothing about the one that runs at 3am. It is also the
/// only way to find out whether the destination credentials work, since saving the policy does
/// not write to the customer's bucket uninvited.
/// </summary>
public sealed class RunBackupNowEndpoint : EndpointWithoutRequest<BackupPolicyDto>
{
    public override void Configure()
    {
        Post("/api/workspace/backup/run");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Run the workspace backup now";
            s.Description = "Workspace admins only. Same path as the scheduled run; the response carries the outcome, including the failure reason if the destination rejected it.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var policy = await db.WorkspaceBackupPolicies
            .Include(p => p.Workspace)
            .FirstOrDefaultAsync(p => p.WorkspaceId == seat.WorkspaceId, ct);

        if (policy is null)
        {
            ThrowError(ErrorCodes.BackupPolicyNotFound, StatusCodes.Status404NotFound);
            return;
        }

        await Resolve<WorkspaceBackupJob>().RunOneAsync(db, policy, ct);

        // 200 even when the run failed: the failure is the answer the admin asked for, and it
        // is in the body. A 500 here would say the request was broken, which it was not.
        await Send.OkAsync(GetBackupPolicyEndpoint.Map(policy), ct);
    }
}

/// <summary>DELETE /api/workspace/backup — stop backing up and forget the destination (issue #271).</summary>
public sealed class DeleteBackupPolicyEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/backup");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Remove the workspace backup policy";
            s.Description = "Workspace admins only. Manifests already written to your bucket are left alone — they are yours.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var policy = await db.WorkspaceBackupPolicies
            .FirstOrDefaultAsync(p => p.WorkspaceId == seat.WorkspaceId, ct);

        if (policy is null)
        {
            ThrowError(ErrorCodes.BackupPolicyNotFound, StatusCodes.Status404NotFound);
            return;
        }

        db.WorkspaceBackupPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.WorkspaceBackupPolicyChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { removed = true }));

        await Send.NoContentAsync(ct);
    }
}
