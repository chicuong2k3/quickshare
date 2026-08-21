using QuickShare.Features.Workspaces.Manage;

namespace QuickShare.Features.Workspaces.Report;

public sealed record WorkspaceMemberUsage(
    Guid UserId,
    string Name,
    string Email,
    string Role,
    int Deliveries,
    long BytesUploaded,
    int Downloads,
    DateTime? LastActivityAt);

public sealed record WorkspaceFileTypeUsage(string ContentType, int Deliveries, long Bytes);

public sealed record WorkspaceReportDto(
    Guid WorkspaceId,
    string Name,
    string Plan,
    int WindowDays,
    DateTime GeneratedAt,
    int Seats,
    int SeatsUsed,
    int SeatsPending,
    long PooledQuotaBytes,
    long PooledUsedBytes,
    int Deliveries,
    long BytesUploaded,
    int Downloads,
    IReadOnlyList<WorkspaceMemberUsage> Members,
    IReadOnlyList<WorkspaceMemberUsage> InactiveMembers,
    IReadOnlyList<WorkspaceFileTypeUsage> FileTypes);

/// <summary>
/// Builds the workspace health report (issue #300). One query set, used by the on-demand
/// view, the CSV export and the scheduled email — a second implementation for the email
/// would be a second set of numbers, and an admin comparing the two would be right to
/// distrust both.
///
/// The window is a rolling N days of delivery activity; the quota figures are the current
/// billing month, because that is the period the plan cap actually resets on.
/// </summary>
public sealed class WorkspaceReportService(AppDbContext db)
{
    public const int DefaultWindowDays = 30;
    public const int MinWindowDays = 1;
    public const int MaxWindowDays = 365;
    /// <summary>A seat with no delivery in the whole window is the offboarding signal.</summary>
    public const int TopFileTypes = 8;

    public async Task<WorkspaceReportDto?> BuildAsync(Guid workspaceId, int windowDays, CancellationToken ct)
    {
        var workspace = await db.Workspaces
            .AsNoTracking()
            .Include(w => w.Owner)
            .FirstOrDefaultAsync(w => w.Id == workspaceId, ct);
        if (workspace is null)
            return null;

        var days = Math.Clamp(windowDays, MinWindowDays, MaxWindowDays);
        var now = DateTime.UtcNow;
        var since = now.AddDays(-days);

        var seats = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.RevokedAt == null)
            .Select(m => new
            {
                m.UserId,
                m.InviteEmail,
                m.Role,
                m.JoinedAt,
                Name = db.Users.Where(u => u.Id == m.UserId).Select(u => u.Name).FirstOrDefault() ?? string.Empty,
            })
            .ToListAsync(ct);

        // Workspace deliveries only. Personal shares by the same accounts are deliberately
        // out of scope — an admin report is not a licence to read private history (#187).
        var deliveries = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.WorkspaceId == workspaceId && d.CreatedAt >= since)
            .Select(d => new
            {
                d.SenderId,
                d.FileSize,
                d.DownloadCount,
                d.ContentType,
                d.CreatedAt,
            })
            .ToListAsync(ct);

        var members = seats
            .Where(s => s.UserId is not null && s.JoinedAt is not null)
            .Select(s =>
            {
                var mine = deliveries.Where(d => d.SenderId == s.UserId).ToList();
                return new WorkspaceMemberUsage(
                    s.UserId!.Value,
                    s.Name,
                    s.InviteEmail,
                    s.Role.ToString().ToLowerInvariant(),
                    mine.Count,
                    mine.Sum(d => d.FileSize),
                    mine.Sum(d => d.DownloadCount),
                    mine.Count == 0 ? null : mine.Max(d => d.CreatedAt));
            })
            .OrderByDescending(m => m.BytesUploaded)
            .ToList();

        var fileTypes = deliveries
            .GroupBy(d => string.IsNullOrEmpty(d.ContentType) ? "unknown" : d.ContentType)
            .Select(g => new WorkspaceFileTypeUsage(g.Key, g.Count(), g.Sum(d => d.FileSize)))
            .OrderByDescending(t => t.Bytes)
            .Take(TopFileTypes)
            .ToList();

        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var pooledUsed = await db.UserUsages
            .AsNoTracking()
            .Where(u => u.UserId == workspace.OwnerId && u.PeriodStart == periodStart)
            .Select(u => (long?)u.TotalBytesUploaded)
            .FirstOrDefaultAsync(ct) ?? 0;

        var quota = (PlanTierLimits.GetMonthlyQuota(workspace.Owner.Plan) ?? 0) + workspace.Owner.BonusQuotaBytes;

        return new WorkspaceReportDto(
            workspace.Id,
            workspace.Name,
            workspace.Owner.Plan,
            days,
            now,
            workspace.Seats,
            seats.Count(s => s.JoinedAt is not null),
            seats.Count(s => s.JoinedAt is null),
            quota,
            pooledUsed,
            deliveries.Count,
            deliveries.Sum(d => d.FileSize),
            deliveries.Sum(d => d.DownloadCount),
            members,
            members.Where(m => m.Deliveries == 0).ToList(),
            fileTypes);
    }
}
