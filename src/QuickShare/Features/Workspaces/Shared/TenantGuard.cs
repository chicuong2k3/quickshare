using System.Text.Json;

namespace QuickShare.Features.Workspaces.Shared;

/// <summary>
/// Records that a caller reached for a resource belonging to somebody else (issue #190).
///
/// **The isolation itself is not here.** Every workspace-scoped query already carries its own
/// <c>WorkspaceId</c> or <c>UserId</c> predicate, and that is deliberately the whole mechanism:
/// one explicit <c>Where</c> per query, visible at the call site, rather than an EF global
/// query filter. A global filter is easy to add and easy to lose — <c>IgnoreQueryFilters</c>,
/// a raw SQL query, or a projection built before the filter applies all silently defeat it, and
/// the failure mode is org A reading org B's files with no error anywhere. An explicit
/// predicate can be read, grepped and tested; the isolation test suite is what proves it.
///
/// What this class adds is the *observation*. A scoped lookup that misses cannot tell the
/// caller why, and should not — but the sender's own timeline should say that somebody probed
/// an id they had no business holding, because a burst of those is exactly the signal SOC 2
/// CC7.2 asks to be monitoring for.
/// </summary>
public sealed class TenantGuard(AppDbContext db, IHttpContextAccessor http)
{
    public const string ResourceFolder = "folder";
    public const string ResourceInvoice = "invoice";
    public const string ResourceWorkspace = "workspace";
    public const string ResourceDelivery = "delivery";

    /// <summary>
    /// Notes a blocked cross-tenant reach on the caller's timeline. Fire-and-record: the caller
    /// has already decided to refuse, so this never changes the answer, and the resource id is
    /// stored without any of its contents — logging what they could not read would be its own
    /// small leak.
    /// </summary>
    public async Task RecordAsync(Guid actorId, Guid? workspaceId, string resourceKind, Guid resourceId, CancellationToken ct)
    {
        var context = http.HttpContext;

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = actorId,
            WorkspaceId = workspaceId,
            EventType = SecurityEventType.CrossTenantAccessBlocked,
            IpHash = Users.Shared.DeviceTrackerService.HashIp(
                context?.Connection.RemoteIpAddress?.ToString() ?? string.Empty),
            UserAgentLabel = Users.Shared.DeviceTrackerService.SummarizeUserAgent(
                context?.Request.Headers.UserAgent.ToString() ?? string.Empty),
            MetadataJson = JsonSerializer.Serialize(new { resource = resourceKind, id = resourceId }),
            CreatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Distinguishes "no such folder" from "someone else's folder" and records the latter.
    /// Costs one extra query, and only on the path that already failed — the caller still gets
    /// the same refusal either way, so nothing about the answer tells them which it was.
    /// </summary>
    public async Task NoteFolderProbeAsync(Guid actorId, Guid? workspaceId, Guid folderId, CancellationToken ct)
    {
        var existsElsewhere = await db.WorkspaceFolders
            .AsNoTracking()
            .AnyAsync(f => f.Id == folderId && f.WorkspaceId != workspaceId, ct);

        if (existsElsewhere)
            await RecordAsync(actorId, workspaceId, ResourceFolder, folderId, ct);
    }
}
