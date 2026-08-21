using System.Security.Claims;
using System.Text.Json;

namespace QuickShare.Features.Auth.Sso;

/// <summary>
/// Enterprise OIDC sign-in: directory verification and JIT seat provisioning (issue #181).
///
/// Runs inside the existing OAuth ticket hook rather than as a parallel login stack. The
/// consumer path is untouched — an account with no matching directory claim signs in exactly as
/// before. What this adds is: if the identity provider says "this account belongs to directory
/// X", and a workspace has bound itself to directory X, then the account gets a seat.
///
/// **The claim is the proof, not the email domain.** Anyone can hold a consumer Google account
/// on a custom domain that ends in @acme.com; only Google can put `hd=acme.com` in an ID token.
/// So <see cref="ResolveAsync"/> matches on the directory claim first, and the email-domain list
/// only ever narrows that set further. Getting this backwards is an account-takeover bug: the
/// workspace would hand a seat to anybody who registered the right-looking address.
/// </summary>
public sealed class SsoLoginService(AppDbContext db, ILogger<SsoLoginService> logger)
{
    /// <summary>Google's hosted-domain claim, present only on Workspace accounts.</summary>
    public const string HostedDomainClaim = "hd";
    /// <summary>Entra's tenant id claim.</summary>
    public const string TenantIdClaim = "tid";
    /// <summary>Entra's group-membership claim (an array of object ids).</summary>
    public const string GroupsClaim = "groups";

    /// <summary>
    /// Applies whatever SSO binding matches the directory claims on this ticket. Best-effort:
    /// a provisioning failure must never take down sign-in, because the alternative is that a
    /// misconfigured workspace binding locks the whole deployment out.
    /// </summary>
    public async Task ApplyAsync(Guid userId, string email, string provider, IEnumerable<Claim> claims, CancellationToken ct)
    {
        try
        {
            var claimList = claims.ToList();
            var config = await ResolveAsync(provider, claimList, email, ct);
            if (config is null)
                return;

            await ProvisionAsync(config, userId, email, claimList, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SSO provisioning failed for {UserId}", userId);
        }
    }

    /// <summary>
    /// The workspace binding this ticket qualifies for, or null. Public so the login page and
    /// the tests can ask the same question the sign-in hook asks.
    /// </summary>
    public async Task<WorkspaceSsoConfig?> ResolveAsync(
        string provider, IReadOnlyCollection<Claim> claims, string email, CancellationToken ct)
    {
        var (ssoProvider, directoryId) = ReadDirectory(provider, claims);
        if (ssoProvider is null || string.IsNullOrEmpty(directoryId))
            return null;

        var config = await db.WorkspaceSsoConfigs
            .FirstOrDefaultAsync(c => c.Enabled
                && c.Provider == ssoProvider
                && c.DirectoryId == directoryId, ct);

        if (config is null)
            return null;

        // The narrowing filter, applied only after the directory claim already matched.
        return EmailMatchesDirectory(config, email) ? config : null;
    }

    /// <summary>
    /// Whether an address passes the config's email-domain narrowing. A blank list means the
    /// whole directory qualifies — which is the common case, since the directory is the control.
    /// </summary>
    public static bool EmailMatchesDirectory(WorkspaceSsoConfig config, string email)
    {
        if (string.IsNullOrWhiteSpace(config.AllowedEmailDomains))
            return true;

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
            return false;

        var domain = email[(at + 1)..].Trim();

        foreach (var allowed in config.AllowedEmailDomains.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(allowed, domain, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Reads the directory a ticket claims to belong to. Null when it claims none.</summary>
    private static (string? Provider, string? DirectoryId) ReadDirectory(string provider, IReadOnlyCollection<Claim> claims)
    {
        if (string.Equals(provider, AuthConstants.GoogleProvider, StringComparison.OrdinalIgnoreCase))
        {
            var hd = claims.FirstOrDefault(c => c.Type == HostedDomainClaim)?.Value;
            // A consumer Google account carries no hd. That is the whole distinction.
            return string.IsNullOrEmpty(hd) ? (null, null) : (SsoProvider.GoogleWorkspace, hd);
        }

        if (string.Equals(provider, AuthConstants.EntraProvider, StringComparison.OrdinalIgnoreCase))
        {
            var tid = claims.FirstOrDefault(c => c.Type == TenantIdClaim)?.Value;
            return string.IsNullOrEmpty(tid) ? (null, null) : (SsoProvider.Entra, tid);
        }

        return (null, null);
    }

    /// <summary>
    /// Gives the account a seat, or corrects the role of the seat it already holds.
    ///
    /// Never touches an Owner seat. Ownership carries the bill, and a directory group is not a
    /// mechanism for handing somebody else's subscription around — <c>#252</c>'s transfer flow
    /// exists precisely because that move needs consent.
    /// </summary>
    private async Task ProvisionAsync(
        WorkspaceSsoConfig config, Guid userId, string email, IReadOnlyCollection<Claim> claims, CancellationToken ct)
    {
        if (config.JitRole is null)
            return;

        var role = ResolveRole(config, claims);

        var seat = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == config.WorkspaceId && m.UserId == userId, ct);

        if (seat is not null)
        {
            if (seat.Role == WorkspaceRole.Owner)
                return;

            var changed = false;

            // A revoked seat coming back through the directory is a rehire, not a bypass: the
            // directory is the system of record for who works here.
            if (seat.RevokedAt is not null)
            {
                seat.RevokedAt = null;
                seat.JoinedAt ??= DateTime.UtcNow;
                changed = true;
            }

            if (seat.Role != role)
            {
                seat.Role = role;
                changed = true;
            }

            if (changed)
            {
                await AuditAsync(config.WorkspaceId, userId, SecurityEventType.SsoMemberProvisioned, role, ct);
                await db.SaveChangesAsync(ct);
            }

            return;
        }

        // Seats are finite and paid for. Refusing here rather than overselling keeps the
        // billing invariant that #106 relies on; the admin sees it in the audit log.
        var workspace = await db.Workspaces.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == config.WorkspaceId, ct);
        if (workspace is null)
            return;

        var held = await db.WorkspaceMembers
            .CountAsync(m => m.WorkspaceId == config.WorkspaceId && m.RevokedAt == null, ct);

        if (held >= workspace.Seats)
        {
            await AuditAsync(config.WorkspaceId, userId, SecurityEventType.SsoProvisionRefused, role, ct);
            await db.SaveChangesAsync(ct);
            return;
        }

        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            WorkspaceId = config.WorkspaceId,
            UserId = userId,
            InviteEmail = email,
            Role = role,
            JoinedAt = DateTime.UtcNow,
        });

        await AuditAsync(config.WorkspaceId, userId, SecurityEventType.SsoMemberProvisioned, role, ct);
        await db.SaveChangesAsync(ct);

        await new Workspaces.Shared.WorkspaceService(db).SyncMemberPlansAsync(config.WorkspaceId, ct);
    }

    /// <summary>
    /// Admin group membership wins over the default role. Entra only: Google does not put group
    /// membership in an ID token, so a Workspace binding always provisions at
    /// <see cref="WorkspaceSsoConfig.JitRole"/> and the doc says so.
    /// </summary>
    private static WorkspaceRole ResolveRole(WorkspaceSsoConfig config, IReadOnlyCollection<Claim> claims)
    {
        var fallback = config.JitRole ?? WorkspaceRole.Member;
        if (string.IsNullOrWhiteSpace(config.AdminGroupId))
            return fallback;

        foreach (var claim in claims.Where(c => c.Type == GroupsClaim))
        {
            if (string.Equals(claim.Value, config.AdminGroupId, StringComparison.OrdinalIgnoreCase))
                return WorkspaceRole.Admin;
        }

        return fallback;
    }

    /// <summary>
    /// Records a provisioning decision. <see cref="SecurityEvent.UserId"/> is a foreign key, so
    /// the row is always attributed to a real account — here that is always the account being
    /// provisioned, because SSO provisioning only ever happens during its own sign-in.
    /// </summary>
    private async Task AuditAsync(Guid workspaceId, Guid userId, SecurityEventType type, WorkspaceRole role, CancellationToken ct)
    {
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            WorkspaceId = workspaceId,
            EventType = type,
            IpHash = string.Empty,
            UserAgentLabel = "sso",
            MetadataJson = JsonSerializer.Serialize(new { role = role.ToString() }),
        });

        await Task.CompletedTask;
    }
}
