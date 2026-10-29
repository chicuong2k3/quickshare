using LightResults;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Folders;

public enum FolderMoveDirection
{
    Up,
    Down,
}

public record MoveFolderCommand(Guid FolderId, FolderMoveDirection Direction) : IRequest<Result<bool>>;

public sealed class MoveFolderHandler(AppDbContext db, IHttpContextAccessor http, WorkspaceService workspaces)
    : IRequestHandler<MoveFolderCommand, Result<bool>>
{
    public async ValueTask<Result<bool>> Handle(MoveFolderCommand cmd, CancellationToken cancellationToken)
    {
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, http.HttpContext!, cancellationToken);
        if (seat is null)
        {
            return Result.Failure<bool>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));
        }

        var folder = await db.WorkspaceFolders.AsNoTracking()
            .FirstOrDefaultAsync(f => f.WorkspaceId == seat.WorkspaceId && f.Id == cmd.FolderId, cancellationToken);
        if (folder is null)
        {
            return Result.Failure<bool>(new LightResults.Error(ErrorCodes.FolderNotFound));
        }

        if (cmd.Direction is not (FolderMoveDirection.Up or FolderMoveDirection.Down))
        {
            return Result.Failure<bool>(new LightResults.Error(ErrorCodes.BadRequest));
        }

        var siblings = await db.WorkspaceFolders
            .Where(f => f.WorkspaceId == seat.WorkspaceId && f.ParentId == folder.ParentId)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.Name)
            .ToListAsync(cancellationToken);
        var index = siblings.FindIndex(f => f.Id == folder.Id);
        var target = cmd.Direction == FolderMoveDirection.Up ? index - 1 : index + 1;
        if (index < 0 || target < 0 || target >= siblings.Count)
        {
            return Result.Success(false);
        }

        (siblings[index], siblings[target]) = (siblings[target], siblings[index]);
        for (var position = 0; position < siblings.Count; position++)
        {
            siblings[position].SortOrder = position;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success(true);
    }
}
