using System.Text.Json;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Protocols.S3;

/// <summary>An access key pair. <c>Secret</c> is populated exactly once, when it is created.</summary>
public sealed record S3CredentialDto(
    Guid Id, string Label, string AccessKeyId, string Scopes,
    DateTime CreatedAt, DateTime? LastUsedAt, string? Secret);

public sealed class CreateS3CredentialRequest
{
    public string? Label { get; set; }
    public string? Scopes { get; set; }
}

/// <summary>GET /api/workspace/s3-credentials — the workspace's gateway keys (issue #265).</summary>
public sealed class ListS3CredentialsEndpoint : EndpointWithoutRequest<IReadOnlyList<S3CredentialDto>>
{
    public override void Configure()
    {
        Get("/api/workspace/s3-credentials");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "List the workspace's S3 access keys";
            s.Description = "Workspace admins only. Secrets are never returned after creation.";
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

        var credentials = await db.WorkspaceS3Credentials
            .AsNoTracking()
            .Where(c => c.WorkspaceId == seat.WorkspaceId && c.RevokedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new S3CredentialDto(c.Id, c.Label, c.AccessKeyId, c.Scopes, c.CreatedAt, c.LastUsedAt, null))
            .ToListAsync(ct);

        await Send.OkAsync(credentials, ct);
    }
}

/// <summary>
/// POST /api/workspace/s3-credentials — issue a pair (issue #265).
///
/// The gateway acts as the admin who created the key: quota, plan limits and the audit trail all
/// follow that account. A shared workspace credential with no person behind it would leave every
/// uploaded object attributable to nobody.
/// </summary>
public sealed class CreateS3CredentialEndpoint : Endpoint<CreateS3CredentialRequest, S3CredentialDto>
{
    private const int MaxPerWorkspace = 5;

    public override void Configure()
    {
        Post("/api/workspace/s3-credentials");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Issue an S3 access key pair";
            s.Description = "Workspace admins only. The secret is returned once and cannot be retrieved again.";
        });
    }

    public override async Task HandleAsync(CreateS3CredentialRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var live = await db.WorkspaceS3Credentials
            .CountAsync(c => c.WorkspaceId == seat.WorkspaceId && c.RevokedAt == null, ct);

        // A ceiling, because every live pair is a reversible secret at rest. Five is more than a
        // team needs and few enough that "which of these is still in use" stays answerable.
        if (live >= MaxPerWorkspace)
        {
            ThrowError(ErrorCodes.S3CredentialLimitReached, StatusCodes.Status409Conflict);
            return;
        }

        var label = (req.Label ?? string.Empty).Trim();
        if (label.Length == 0 || label.Length > WorkspaceS3Credential.LabelMaxLength)
        {
            ThrowError(ErrorCodes.S3CredentialLabelInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var (credential, secret) = await Resolve<S3Credentials>()
            .IssueAsync(seat.WorkspaceId, seat.UserId!.Value, label, req.Scopes ?? ApiScopes.Read, ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.S3CredentialIssued, HttpContext, ct,
            JsonSerializer.Serialize(new { credential.AccessKeyId, credential.Scopes }));

        await Send.OkAsync(new S3CredentialDto(
            credential.Id, credential.Label, credential.AccessKeyId, credential.Scopes,
            credential.CreatedAt, null, secret), ct);
    }
}

/// <summary>DELETE /api/workspace/s3-credentials/{Id} — revoke a pair (issue #265).</summary>
public sealed class RevokeS3CredentialEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/s3-credentials/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Revoke an S3 access key pair");
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

        var id = Route<Guid>("Id");
        var credential = await db.WorkspaceS3Credentials
            .FirstOrDefaultAsync(c => c.Id == id && c.WorkspaceId == seat.WorkspaceId && c.RevokedAt == null, ct);

        if (credential is null)
        {
            ThrowError(ErrorCodes.S3CredentialNotFound, StatusCodes.Status404NotFound);
            return;
        }

        // Revoked, not deleted: the audit rows that name this access key stay readable.
        credential.RevokedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value,
            SecurityEventType.S3CredentialRevoked, HttpContext, ct,
            JsonSerializer.Serialize(new { credential.AccessKeyId }));

        await Send.NoContentAsync(ct);
    }
}
