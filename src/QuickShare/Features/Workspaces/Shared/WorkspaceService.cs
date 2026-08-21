using System.Security.Cryptography;
using System.Text;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Workspaces.Shared;

/// <summary>
/// The one place that knows what a workspace seat means (issue #106).
///
/// Two decisions live here and nowhere else. First, the plan: an active seat copies the
/// owner's plan onto <see cref="User.Plan"/> and parks the member's own plan in
/// <see cref="User.PersonalPlan"/>. Every existing tier gate reads <c>user.Plan</c>, so
/// inheritance costs no call-site changes and cannot silently miss one — the price is a
/// denormalized column, which is why the owner's plan is re-pushed from
/// <see cref="SyncMemberPlansAsync"/> on every subscription change.
///
/// Second, the audit: workspace actions land on the same append-only store as the personal
/// security timeline (issue #145), stamped with the workspace id so the admin lens is a
/// filter rather than a second write path that could drift out of step.
/// </summary>
public sealed class WorkspaceService(AppDbContext db)
{
    /// <summary>SHA-256 hex of an invite or transfer token. Raw tokens are never stored.</summary>
    public static string HashToken(string token)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creating or owning a workspace is a paid-plan feature — the owner is the bill.</summary>
    public static bool CanOwnWorkspace(string plan)
        => plan is PlanTierLimits.Pro or PlanTierLimits.Studio;

    /// <summary>
    /// The caller's live seat, or null when they are not in a workspace. Includes the
    /// workspace so callers can read the owner and the plan without a second round trip.
    /// </summary>
    public async Task<WorkspaceMember?> GetSeatAsync(Guid userId, CancellationToken ct)
        => await db.WorkspaceMembers
            .Include(m => m.Workspace)
            .ThenInclude(w => w.Owner)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.RevokedAt == null && m.JoinedAt != null, ct);

    /// <summary>Seats held: accepted members plus outstanding invites, owner included.</summary>
    public Task<int> CountHeldSeatsAsync(Guid workspaceId, CancellationToken ct)
        => db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId && m.RevokedAt == null, ct);

    /// <summary>
    /// Re-pushes the owner's plan onto every active seat (issue #106). Called whenever the
    /// owner's plan changes and whenever the membership set changes, because
    /// <see cref="User.Plan"/> is a copy and a copy has to be refreshed to stay honest.
    /// </summary>
    public async Task SyncMemberPlansAsync(Guid workspaceId, CancellationToken ct)
    {
        var workspace = await db.Workspaces
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (workspace is null)
            return;

        var plan = workspace.Owner.Plan;
        var memberIds = await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId
                && m.RevokedAt == null
                && m.JoinedAt != null
                && m.UserId != null
                && m.UserId != workspace.OwnerId)
            .Select(m => m.UserId!.Value)
            .ToListAsync(ct);

        if (memberIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        // Remember the member's own plan the first time a seat overrides it, so a later
        // revoke restores what they actually pay for instead of dropping them to Free.
        await db.Users
            .Where(u => memberIds.Contains(u.Id) && u.PersonalPlan == null)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PersonalPlan, u => u.Plan), ct);

        await db.Users
            .Where(u => memberIds.Contains(u.Id) && u.Plan != plan)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, plan)
                .SetProperty(u => u.UpdatedAt, now), ct);

        foreach (var id in memberIds)
            await UserCacheInvalidator.EvictAsync(db, id, ct);
    }

    /// <summary>
    /// Hands an account back its own plan after a seat ends. Restoring is not the same as
    /// downgrading to Free: a member who pays for Pro personally keeps Pro.
    /// </summary>
    public async Task RestorePersonalPlanAsync(Guid userId, CancellationToken ct)
    {
        var personal = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.PersonalPlan)
            .FirstOrDefaultAsync(ct);

        if (personal is null)
            return;

        var now = DateTime.UtcNow;
        await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.Plan, personal)
                .SetProperty(u => u.PersonalPlan, (string?)null)
                .SetProperty(u => u.UpdatedAt, now), ct);

        await UserCacheInvalidator.EvictAsync(db, userId, ct);
    }

    /// <summary>
    /// Appends one workspace audit row (issue #145) on the same store as the personal
    /// timeline. Immutable, 90-day retention via the existing SecurityEventCleanupJob.
    /// </summary>
    public async Task AuditAsync(
        Guid workspaceId,
        Guid actorId,
        SecurityEventType type,
        HttpContext? http,
        CancellationToken ct,
        string? metadataJson = null)
    {
        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = actorId,
            WorkspaceId = workspaceId,
            EventType = type,
            IpHash = DeviceTrackerService.HashIp(http?.Connection.RemoteIpAddress?.ToString() ?? string.Empty),
            UserAgentLabel = DeviceTrackerService.SummarizeUserAgent(
                http?.Request.Headers.UserAgent.ToString() ?? string.Empty),
            MetadataJson = metadataJson,
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }
}
