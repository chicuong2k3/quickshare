using QuickShare.Features.Auth.Totp;

namespace QuickShare.Features.Auth.Mfa;

/// <summary>
/// What a workspace's MFA policy currently means for one member (issue #219).
/// </summary>
/// <param name="Required">A policy applies to this account at all.</param>
/// <param name="Enrolled">The account holds a confirmed authenticator.</param>
/// <param name="SsoExempt">
/// The account signs in through the workspace's SSO directory (issue #181), which is already a
/// second factor enforced by the directory. Requiring a second one here would be theatre the
/// directory admin cannot switch off.
/// </param>
/// <param name="GraceEndsAt">When enforcement starts biting. Null when it already has.</param>
/// <param name="Blocked">Un-enrolled, out of grace: the account is held out of the workspace.</param>
public readonly record struct MfaVerdict(
    bool Required,
    bool Enrolled,
    bool SsoExempt,
    DateTime? GraceEndsAt,
    bool Blocked)
{
    /// <summary>Policy applies, not yet enrolled, still inside the window — show the nudge.</summary>
    public bool InGrace => Required && !Enrolled && !SsoExempt && !Blocked;
}

/// <summary>
/// Resolves the MFA policy for an account (issue #219).
///
/// The rule the whole feature turns on: enforcement is measured from when the policy started
/// applying to *this member*, not from when the admin ticked the box. A member who joins on
/// Tuesday gets their own grace window, or the policy would lock out every future hire the
/// instant they accept an invite.
///
/// Read-only and side-effect free, because it is called from the request path on nearly every
/// workspace operation.
/// </summary>
public sealed class MfaPolicyService(AppDbContext db)
{
    /// <summary>
    /// Nothing applies to an account with no workspace seat. Personal accounts can enrol — the
    /// policy is a workspace control, and inventing an org rule for a solo account would be a
    /// lockout with nobody to appeal to.
    /// </summary>
    public static readonly MfaVerdict NotApplicable = new(false, false, false, null, false);

    public async Task<MfaVerdict> EvaluateAsync(Guid userId, CancellationToken ct)
    {
        var seat = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId && m.JoinedAt != null && m.RevokedAt == null)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (seat is null)
            return NotApplicable;

        var workspace = await db.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == seat.WorkspaceId, ct);

        if (workspace is null || !workspace.MfaRequired)
            return NotApplicable;

        var enrolled = await db.UserTotps
            .AsNoTracking()
            .AnyAsync(t => t.UserId == userId && t.ConfirmedAt != null, ct);

        var ssoExempt = await IsSsoMemberAsync(userId, seat.WorkspaceId, ct);

        if (enrolled || ssoExempt)
            return new MfaVerdict(true, enrolled, ssoExempt, null, false);

        // Later of "policy switched on" and "member joined": whichever started applying last.
        var appliesFrom = workspace.MfaRequiredAt ?? workspace.UpdatedAt ?? workspace.CreatedAt;
        var joined = seat.JoinedAt ?? seat.CreatedAt;
        var start = appliesFrom > joined ? appliesFrom : joined;
        var graceEnds = start.AddDays(workspace.MfaGraceDays);

        return graceEnds > DateTime.UtcNow
            ? new MfaVerdict(true, false, false, graceEnds, false)
            : new MfaVerdict(true, false, false, graceEnds, true);
    }

    /// <summary>
    /// True when this account's email sits in the workspace's SSO directory. The directory claim
    /// is checked at sign-in (<see cref="Sso.SsoLoginService"/>); this is the durable trace of it,
    /// so the exemption survives a page load that has no ID token to hand.
    /// </summary>
    private async Task<bool> IsSsoMemberAsync(Guid userId, Guid workspaceId, CancellationToken ct)
    {
        var config = await db.WorkspaceSsoConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.Enabled, ct);

        if (config is null)
            return false;

        var email = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);

        return !string.IsNullOrEmpty(email) && Sso.SsoLoginService.EmailMatchesDirectory(config, email);
    }

    /// <summary>
    /// Enrolment rate across a workspace, for the compliance card and its SOC 2 export (#219).
    /// SSO members count as covered: the directory enforces their second factor.
    /// </summary>
    public async Task<(int Members, int Covered)> PostureAsync(Guid workspaceId, CancellationToken ct)
    {
        var memberIds = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.JoinedAt != null && m.RevokedAt == null && m.UserId != null)
            .Select(m => m.UserId!.Value)
            .ToListAsync(ct);

        if (memberIds.Count == 0)
            return (0, 0);

        var enrolled = await db.UserTotps
            .AsNoTracking()
            .Where(t => memberIds.Contains(t.UserId) && t.ConfirmedAt != null)
            .Select(t => t.UserId)
            .ToListAsync(ct);

        var covered = new HashSet<Guid>(enrolled);

        var config = await db.WorkspaceSsoConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkspaceId == workspaceId && c.Enabled, ct);

        if (config is not null)
        {
            var directoryUsers = await db.Users
                .AsNoTracking()
                .Where(u => memberIds.Contains(u.Id))
                .Select(u => new { u.Id, u.Email })
                .ToListAsync(ct);

            foreach (var candidate in directoryUsers)
            {
                if (Sso.SsoLoginService.EmailMatchesDirectory(config, candidate.Email))
                    covered.Add(candidate.Id);
            }
        }

        return (memberIds.Count, covered.Count);
    }
}
