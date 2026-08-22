using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Folders;

public sealed class ReorderFoldersRequest
{
    /// <summary>Folder ids in the order they should appear. Anything missing keeps its place.</summary>
    public List<Guid>? Order { get; set; }
}

/// <summary>
/// PATCH /api/workspace/folders/order — the order an admin arranged (issue #275).
///
/// Takes the whole list rather than a "move this one to index 4" instruction: a reorder is one
/// intention, and applying it as a series of moves means every intermediate state is a real state
/// somebody could see. One request, one arrangement.
///
/// Positions are assigned from the given sequence rather than trusted from the client, so a
/// duplicate or a gap in what arrives cannot produce a list that sorts differently for the next
/// reader than it did for the one who dragged it.
/// </summary>
public sealed class ReorderFoldersEndpoint : Endpoint<ReorderFoldersRequest>
{
    public override void Configure()
    {
        Patch("/api/workspace/folders/order");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Set the manual order of the workspace's folders";
            s.Description = "Workspace admins only (issue #275). Send every folder id in the order it should appear.";
        });
    }

    public override async Task HandleAsync(ReorderFoldersRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var order = (req.Order ?? []).Distinct().ToList();
        if (order.Count == 0)
        {
            ThrowError(ErrorCodes.BadRequest, StatusCodes.Status400BadRequest);
            return;
        }

        // Scoped to the caller's own workspace: an id from somebody else's tree is silently not
        // found rather than reordered, which is the same answer as it never existing.
        var folders = await db.WorkspaceFolders
            .Where(f => f.WorkspaceId == seat.WorkspaceId && order.Contains(f.Id))
            .ToListAsync(ct);

        for (var position = 0; position < order.Count; position++)
        {
            var folder = folders.FirstOrDefault(f => f.Id == order[position]);
            if (folder is null)
                continue;

            folder.SortOrder = position;
            folder.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}
