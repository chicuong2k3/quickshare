using System.Security.Cryptography;
using System.Text.Json;
using QuickShare.Features.Auth.Mfa;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Auth.Sso;

/// <summary>The workspace's SSO binding. Carries no secret — an OIDC client secret is deployment config.</summary>
public sealed record SsoConfigDto(
    string Provider,
    string DirectoryId,
    string? AllowedEmailDomains,
    bool Enabled,
    bool RequireSso,
    string? JitRole,
    string? AdminGroupId);

/// <summary>MFA policy plus the posture numbers a SOC 2 reviewer asks for (issue #219).</summary>
public sealed record MfaPolicyDto(
    bool MfaRequired,
    int MfaGraceDays,
    DateTime? MfaRequiredAt,
    int Members,
    int Covered);

/// <summary>A SCIM token. The raw value appears only in the response that creates it.</summary>
public sealed record ScimTokenDto(Guid Id, string Label, DateTime CreatedAt, DateTime? LastUsedAt, string? Token);

public sealed class SsoConfigRequest
{
    public string? Provider { get; set; }
    public string? DirectoryId { get; set; }
    public string? AllowedEmailDomains { get; set; }
    public bool Enabled { get; set; } = true;
    public bool RequireSso { get; set; }
    /// <summary>Owner / Admin / Member / Viewer, or null to switch JIT provisioning off.</summary>
    public string? JitRole { get; set; } = nameof(WorkspaceRole.Member);
    public string? AdminGroupId { get; set; }
}

public sealed class MfaPolicyRequest
{
    public bool MfaRequired { get; set; }
    public int? MfaGraceDays { get; set; }
}

public sealed class ScimTokenRequest
{
    public string? Label { get; set; }
}

/// <summary>GET /api/workspace/sso — the workspace's SSO binding and MFA policy (issues #181, #219).</summary>
public sealed class GetSsoConfigEndpoint : EndpointWithoutRequest<SsoConfigDto?>
{
    public override void Configure()
    {
        Get("/api/workspace/sso");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Get the workspace's enterprise SSO binding");
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

        var config = await db.WorkspaceSsoConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == seat.WorkspaceId, ct);

        await Send.OkAsync(config is null ? null : Map(config), ct);
    }

    internal static SsoConfigDto Map(WorkspaceSsoConfig config) => new(
        config.Provider,
        config.DirectoryId,
        config.AllowedEmailDomains,
        config.Enabled,
        config.RequireSso,
        config.JitRole?.ToString(),
        config.AdminGroupId);
}

/// <summary>
/// PUT /api/workspace/sso — bind the workspace to a directory (issue #181).
///
/// Refuses to switch on SSO-only when the owner's own address would not pass the directory
/// filter. The failure mode being avoided is specific and unrecoverable through the product: the
/// admin turns it on, gets logged out on their next request, and there is nobody left with the
/// rights to turn it off.
/// </summary>
public sealed class UpdateSsoConfigEndpoint : Endpoint<SsoConfigRequest, SsoConfigDto>
{
    public override void Configure()
    {
        Put("/api/workspace/sso");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Bind the workspace to a Google Workspace domain or Entra tenant";
            s.Description = "Workspace admins only. Membership is proved by the provider's directory claim (hd / tid), never by the email domain alone.";
        });
    }

    public override async Task HandleAsync(SsoConfigRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var provider = (req.Provider ?? SsoProvider.GoogleWorkspace).Trim().ToLowerInvariant();
        if (!SsoProvider.All.Contains(provider))
        {
            ThrowError(ErrorCodes.SsoProviderInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var directoryId = (req.DirectoryId ?? string.Empty).Trim();
        if (directoryId.Length == 0 || directoryId.Length > WorkspaceSsoConfig.DirectoryIdMaxLength)
        {
            ThrowError(ErrorCodes.SsoDirectoryRequired, StatusCodes.Status400BadRequest);
            return;
        }

        WorkspaceRole? jitRole = null;
        if (!string.IsNullOrWhiteSpace(req.JitRole))
        {
            if (!Enum.TryParse<WorkspaceRole>(req.JitRole, ignoreCase: true, out var parsed) || parsed == WorkspaceRole.Owner)
            {
                // Owner is never assignable from a directory: it carries the bill (#252).
                ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
                return;
            }
            jitRole = parsed;
        }

        // A directory belongs to one workspace. Without this, any workspace could bind itself to
        // acme.com and quietly absorb every acme.com account that signed in afterwards — the
        // directory claim would be proved and then handed to the wrong tenant.
        var claimedElsewhere = await db.WorkspaceSsoConfigs.AnyAsync(
            c => c.Provider == provider && c.DirectoryId == directoryId && c.WorkspaceId != seat.WorkspaceId, ct);

        if (claimedElsewhere)
        {
            ThrowError(ErrorCodes.SsoDirectoryTaken, StatusCodes.Status409Conflict);
            return;
        }

        var config = await db.WorkspaceSsoConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == seat.WorkspaceId, ct);
        var isNew = config is null;

        config ??= new WorkspaceSsoConfig { WorkspaceId = seat.WorkspaceId };
        config.Provider = provider;
        config.DirectoryId = directoryId;
        config.AllowedEmailDomains = string.IsNullOrWhiteSpace(req.AllowedEmailDomains) ? null : req.AllowedEmailDomains.Trim();
        config.Enabled = req.Enabled;
        config.RequireSso = req.RequireSso;
        config.JitRole = jitRole;
        config.AdminGroupId = string.IsNullOrWhiteSpace(req.AdminGroupId) ? null : req.AdminGroupId.Trim();

        if (config.RequireSso && !await OwnerPassesAsync(db, seat.WorkspaceId, config, ct))
        {
            ThrowError(ErrorCodes.SsoWouldLockOutOwner, StatusCodes.Status409Conflict);
            return;
        }

        if (isNew)
            db.WorkspaceSsoConfigs.Add(config);

        var user = await HttpContext.User.GetUserAsync(db, ct);
        await Resolve<WorkspaceService>().AuditAsync(
            seat.WorkspaceId, user.Id, SecurityEventType.SsoConfigChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { provider, directoryId, config.RequireSso }));

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(GetSsoConfigEndpoint.Map(config), ct);
    }

    private static async Task<bool> OwnerPassesAsync(AppDbContext db, Guid workspaceId, WorkspaceSsoConfig config, CancellationToken ct)
    {
        var ownerEmail = await db.Workspaces
            .AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Owner.Email)
            .FirstOrDefaultAsync(ct);

        return !string.IsNullOrEmpty(ownerEmail) && SsoLoginService.EmailMatchesDirectory(config, ownerEmail);
    }
}

/// <summary>DELETE /api/workspace/sso — unbind the directory (issue #181).</summary>
public sealed class DeleteSsoConfigEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/sso");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Remove the workspace's SSO binding";
            s.Description = "Seats already provisioned stay — unbinding stops future sign-ins from provisioning, it does not offboard anyone.";
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

        var config = await db.WorkspaceSsoConfigs.FirstOrDefaultAsync(c => c.WorkspaceId == seat.WorkspaceId, ct);
        if (config is null)
        {
            ThrowError(ErrorCodes.SsoNotConfigured, StatusCodes.Status404NotFound);
            return;
        }

        db.WorkspaceSsoConfigs.Remove(config);

        var user = await HttpContext.User.GetUserAsync(db, ct);
        await Resolve<WorkspaceService>().AuditAsync(
            seat.WorkspaceId, user.Id, SecurityEventType.SsoConfigChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { removed = true }));

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>GET /api/workspace/mfa — the MFA policy and the workspace's enrolment posture (#219).</summary>
public sealed class GetMfaPolicyEndpoint : EndpointWithoutRequest<MfaPolicyDto>
{
    public override void Configure()
    {
        Get("/api/workspace/mfa");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Get the workspace MFA policy and enrolment posture";
            s.Description = "The covered count includes SSO members — their directory already enforces a second factor.";
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

        var workspace = seat.Workspace;
        var (members, covered) = await Resolve<MfaPolicyService>().PostureAsync(seat.WorkspaceId, ct);

        await Send.OkAsync(new MfaPolicyDto(
            workspace.MfaRequired, workspace.MfaGraceDays, workspace.MfaRequiredAt, members, covered), ct);
    }
}

/// <summary>
/// PUT /api/workspace/mfa — require a second factor across the workspace (issue #219).
///
/// Switching it on stamps <c>MfaRequiredAt</c>; switching it off and on again restarts everyone's
/// grace window, which is the honest behaviour — an admin who turns the policy off has told the
/// team it does not apply, and turning it back on is a new ask, not a resumed one.
/// </summary>
public sealed class UpdateMfaPolicyEndpoint : Endpoint<MfaPolicyRequest, MfaPolicyDto>
{
    public override void Configure()
    {
        Put("/api/workspace/mfa");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Set the workspace MFA policy";
            s.Description = "Workspace admins only. Members get a grace period to enrol before they are held out.";
        });
    }

    public override async Task HandleAsync(MfaPolicyRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var grace = req.MfaGraceDays ?? Workspace.DefaultMfaGraceDays;
        if (grace < 0 || grace > Workspace.MaxMfaGraceDays)
        {
            ThrowError(ErrorCodes.MfaGraceInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var workspace = await db.Workspaces.FirstAsync(w => w.Id == seat.WorkspaceId, ct);
        var wasRequired = workspace.MfaRequired;

        workspace.MfaRequired = req.MfaRequired;
        workspace.MfaGraceDays = grace;

        if (req.MfaRequired && !wasRequired)
            workspace.MfaRequiredAt = DateTime.UtcNow;
        else if (!req.MfaRequired)
            workspace.MfaRequiredAt = null;

        var user = await HttpContext.User.GetUserAsync(db, ct);
        await Resolve<WorkspaceService>().AuditAsync(
            seat.WorkspaceId, user.Id, SecurityEventType.MfaPolicyChanged, HttpContext, ct,
            JsonSerializer.Serialize(new { required = req.MfaRequired, graceDays = grace }));

        await db.SaveChangesAsync(ct);

        var (members, covered) = await Resolve<MfaPolicyService>().PostureAsync(seat.WorkspaceId, ct);
        await Send.OkAsync(new MfaPolicyDto(
            workspace.MfaRequired, workspace.MfaGraceDays, workspace.MfaRequiredAt, members, covered), ct);
    }
}

/// <summary>GET /api/workspace/scim-tokens — the workspace's provisioning credentials (issue #146).</summary>
public sealed class ListScimTokensEndpoint : EndpointWithoutRequest<IReadOnlyList<ScimTokenDto>>
{
    public override void Configure()
    {
        Get("/api/workspace/scim-tokens");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "List active SCIM provisioning tokens");
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

        var tokens = await db.WorkspaceScimTokens
            .AsNoTracking()
            .Where(t => t.WorkspaceId == seat.WorkspaceId && t.RevokedAt == null)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ScimTokenDto(t.Id, t.Label, t.CreatedAt, t.LastUsedAt, null))
            .ToListAsync(ct);

        await Send.OkAsync(tokens, ct);
    }
}

/// <summary>
/// POST /api/workspace/scim-tokens — issue a provisioning credential (issue #146).
///
/// The raw token comes back exactly once. It grants seat creation and revocation for this
/// workspace, so it is stored as a hash and there is no endpoint that can read it back —
/// losing it means issuing a new one, which is the correct cost.
/// </summary>
public sealed class CreateScimTokenEndpoint : Endpoint<ScimTokenRequest, ScimTokenDto>
{
    public override void Configure()
    {
        Post("/api/workspace/scim-tokens");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Issue a SCIM provisioning token";
            s.Description = "Returns the raw token once. Paste it into the identity provider's SCIM configuration.";
        });
    }

    public override async Task HandleAsync(ScimTokenRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var raw = Base64UrlToken();
        var label = string.IsNullOrWhiteSpace(req.Label)
            ? "SCIM"
            : req.Label.Trim()[..Math.Min(req.Label.Trim().Length, WorkspaceScimToken.LabelMaxLength)];

        var token = new WorkspaceScimToken
        {
            WorkspaceId = seat.WorkspaceId,
            TokenHash = WorkspaceService.HashToken(raw),
            Label = label,
        };
        db.WorkspaceScimTokens.Add(token);

        var user = await HttpContext.User.GetUserAsync(db, ct);
        await Resolve<WorkspaceService>().AuditAsync(
            seat.WorkspaceId, user.Id, SecurityEventType.ScimTokenIssued, HttpContext, ct,
            JsonSerializer.Serialize(new { label }));

        await db.SaveChangesAsync(ct);

        await Send.OkAsync(new ScimTokenDto(token.Id, token.Label, token.CreatedAt, null, raw), ct);
    }

    private static string Base64UrlToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(WorkspaceScimToken.TokenBytes))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}

/// <summary>DELETE /api/workspace/scim-tokens/{Id} — revoke a provisioning credential (issue #146).</summary>
public sealed class RevokeScimTokenEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/workspace/scim-tokens/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Revoke a SCIM provisioning token");
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

        var id = Route<Guid>("Id");
        // Scoped to the caller's workspace, not looked up by id alone — one workspace must not
        // be able to revoke another's provisioning (issue #190).
        var token = await db.WorkspaceScimTokens
            .FirstOrDefaultAsync(t => t.Id == id && t.WorkspaceId == seat.WorkspaceId && t.RevokedAt == null, ct);

        if (token is null)
        {
            ThrowError(ErrorCodes.ScimTokenNotFound, StatusCodes.Status404NotFound);
            return;
        }

        token.RevokedAt = DateTime.UtcNow;

        var user = await HttpContext.User.GetUserAsync(db, ct);
        await Resolve<WorkspaceService>().AuditAsync(
            seat.WorkspaceId, user.Id, SecurityEventType.ScimTokenRevoked, HttpContext, ct,
            JsonSerializer.Serialize(new { token.Label }));

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
