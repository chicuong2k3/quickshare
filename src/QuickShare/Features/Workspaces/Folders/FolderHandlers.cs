using System.Text.Json;
using LightResults;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Folders;

public sealed record FolderDto(
    Guid Id,
    Guid? ParentId,
    string Name,
    int Deliveries,
    long Bytes,
    double? DefaultExpiryHours,
    bool DefaultPasswordRequired,
    string? DefaultPasswordHint,
    bool DefaultPrivacyMode,
    bool DefaultsLocked);

public sealed record FolderListDto(IReadOnlyList<FolderDto> Folders, bool CanManage);

/// <summary>What the upload form starts from for a folder (issue #245).</summary>
public sealed record FolderPrefillDto(
    Guid FolderId,
    double? ExpiryHours,
    bool PasswordRequired,
    string? PasswordHint,
    bool PrivacyMode,
    bool Locked);

public record ListFoldersQuery : IRequest<Result<FolderListDto>>;
public record CreateFolderCommand(string? Name, Guid? ParentId) : IRequest<Result<FolderDto>>;
public record RenameFolderCommand(Guid FolderId, string? Name) : IRequest<Result<FolderDto>>;
public record DeleteFolderCommand(Guid FolderId) : IRequest<Result<FolderListDto>>;
public record UpdateFolderDefaultsCommand(
    Guid FolderId,
    double? ExpiryHours,
    bool PasswordRequired,
    string? PasswordHint,
    bool PrivacyMode,
    bool? Locked) : IRequest<Result<FolderDto>>;
public record GetFolderPrefillQuery(Guid FolderId) : IRequest<Result<FolderPrefillDto>>;

/// <summary>
/// Lists the workspace's shared folders (issue #151). Every seat sees the whole tree — that is
/// what "shared" means here; per-folder membership would be a second permission system layered
/// on the workspace roles that already exist, and two systems disagreeing is how someone ends
/// up locked out of a folder they can see in a list.
/// </summary>
public sealed class ListFoldersHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<ListFoldersQuery, Result<FolderListDto>>
{
    public async ValueTask<Result<FolderListDto>> Handle(ListFoldersQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderListDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));

        return Result.Success(await FolderReader.ListAsync(db, seat.WorkspaceId, seat.CanAdminister, cancellationToken));
    }
}

/// <summary>
/// Creates a shared folder (issue #151). Admins only: folders are workspace structure, and a
/// viewer inventing top-level folders is how a shared drive turns into everyone's private one.
/// </summary>
public sealed class CreateFolderHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<CreateFolderCommand, Result<FolderDto>>
{
    public async ValueTask<Result<FolderDto>> Handle(CreateFolderCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var created = await FolderWriter.CreateAsync(
            db, seat.WorkspaceId, user.Id, cmd.Name, cmd.ParentId, defaults: null, cancellationToken);

        if (created.IsFailure(out var error))
            return Result.Failure<FolderDto>(error);

        await db.SaveChangesAsync(cancellationToken);

        created.IsSuccess(out var folder);
        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceFolderCreated,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { folder!.Name, folder.ParentId }));

        return Result.Success(FolderReader.ToDto(folder, deliveries: 0, bytes: 0));
    }
}

/// <summary>Renames a folder (issue #151). Admins only; sibling names stay unique.</summary>
public sealed class RenameFolderHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<RenameFolderCommand, Result<FolderDto>>
{
    public async ValueTask<Result<FolderDto>> Handle(RenameFolderCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var name = FolderWriter.NormalizeName(cmd.Name);
        if (name is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.FolderNameInvalid));

        var folder = await db.WorkspaceFolders
            .FirstOrDefaultAsync(f => f.Id == cmd.FolderId && f.WorkspaceId == seat.WorkspaceId, cancellationToken);
        if (folder is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.FolderNotFound));

        var taken = await db.WorkspaceFolders.AnyAsync(f =>
            f.WorkspaceId == seat.WorkspaceId
            && f.ParentId == folder.ParentId
            && f.Name == name
            && f.Id != folder.Id, cancellationToken);
        if (taken)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.FolderNameTaken));

        folder.Name = name;
        folder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceFolderRenamed,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { folder.Name }));

        return Result.Success(FolderReader.ToDto(folder, deliveries: 0, bytes: 0));
    }
}

/// <summary>
/// Deletes an empty folder (issue #151). Refused while anything is still filed under it —
/// deliveries or a child folder. Cascading here would delete the team's files as a side effect
/// of tidying up the structure, which is not a trade anyone would make on purpose.
/// </summary>
public sealed class DeleteFolderHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<DeleteFolderCommand, Result<FolderListDto>>
{
    public async ValueTask<Result<FolderListDto>> Handle(DeleteFolderCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderListDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<FolderListDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var folder = await db.WorkspaceFolders
            .FirstOrDefaultAsync(f => f.Id == cmd.FolderId && f.WorkspaceId == seat.WorkspaceId, cancellationToken);
        if (folder is null)
            return Result.Failure<FolderListDto>(new LightResults.Error(ErrorCodes.FolderNotFound));

        // Soft-deleted deliveries still count: they are restorable for 24h (#99), and restoring
        // one into a folder that no longer exists would leave a dangling reference.
        var hasDeliveries = await db.Deliveries.AnyAsync(d => d.FolderId == folder.Id, cancellationToken);
        var hasChildren = await db.WorkspaceFolders.AnyAsync(f => f.ParentId == folder.Id, cancellationToken);
        if (hasDeliveries || hasChildren)
            return Result.Failure<FolderListDto>(new LightResults.Error(ErrorCodes.FolderNotEmpty));

        db.WorkspaceFolders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceFolderDeleted,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { folder.Name }));

        return Result.Success(await FolderReader.ListAsync(db, seat.WorkspaceId, canManage: true, cancellationToken));
    }
}

/// <summary>
/// Sets a folder's default upload settings (issue #245). Admins set defaults; only the owner
/// may lock them, because a locked default stops being a convenience and starts being a rule
/// the whole team has to live under.
/// </summary>
public sealed class UpdateFolderDefaultsHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<UpdateFolderDefaultsCommand, Result<FolderDto>>
{
    private const double MinExpiryHours = 1;
    private const double MaxExpiryHours = 24 * 365;

    public async ValueTask<Result<FolderDto>> Handle(UpdateFolderDefaultsCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var folder = await db.WorkspaceFolders
            .FirstOrDefaultAsync(f => f.Id == cmd.FolderId && f.WorkspaceId == seat.WorkspaceId, cancellationToken);
        if (folder is null)
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.FolderNotFound));

        if (cmd.ExpiryHours is { } hours && (hours < MinExpiryHours || hours > MaxExpiryHours))
            return Result.Failure<FolderDto>(new LightResults.Error(ErrorCodes.ExpiryInvalid));

        folder.DefaultExpiryHours = cmd.ExpiryHours;
        folder.DefaultPasswordRequired = cmd.PasswordRequired;
        folder.DefaultPasswordHint = Truncate(cmd.PasswordHint);
        folder.DefaultPrivacyMode = cmd.PrivacyMode;

        // Owner-only switch. An admin leaving it alone keeps whatever the owner decided.
        if (cmd.Locked is { } locked && seat.Role == WorkspaceRole.Owner)
            folder.DefaultsLocked = locked;

        folder.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceFolderDefaultsChanged,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new
            {
                folder.Name,
                folder.DefaultExpiryHours,
                folder.DefaultPasswordRequired,
                folder.DefaultPrivacyMode,
                folder.DefaultsLocked,
            }));

        return Result.Success(FolderReader.ToDto(folder, deliveries: 0, bytes: 0));
    }

    private static string? Truncate(string? hint)
    {
        var trimmed = hint?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        return trimmed.Length > WorkspaceFolder.HintMaxLength
            ? trimmed[..WorkspaceFolder.HintMaxLength]
            : trimmed;
    }
}

/// <summary>
/// What the upload form should start from (issue #245). Any seat may read this — a member who
/// can upload into the folder needs to see the settings their upload will carry.
/// </summary>
public sealed class GetFolderPrefillHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces,
    TenantGuard tenants)
    : IRequestHandler<GetFolderPrefillQuery, Result<FolderPrefillDto>>
{
    public async ValueTask<Result<FolderPrefillDto>> Handle(GetFolderPrefillQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<FolderPrefillDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));

        var defaults = await FolderDefaults.ResolveAsync(db, seat.WorkspaceId, query.FolderId, cancellationToken);
        if (defaults is null)
        {
            // The refusal is identical either way — the caller never learns whether the folder
            // exists elsewhere — but a reach into another workspace is worth a timeline entry
            // (issue #190). A burst of them is the signal, not any single one.
            await tenants.NoteFolderProbeAsync(
                user.Id, seat.WorkspaceId, query.FolderId, cancellationToken);

            return Result.Failure<FolderPrefillDto>(new LightResults.Error(ErrorCodes.FolderNotFound));
        }

        return Result.Success(new FolderPrefillDto(
            query.FolderId,
            defaults.ExpiryHours,
            defaults.PasswordRequired,
            defaults.PasswordHint,
            defaults.PrivacyMode,
            defaults.Locked));
    }
}

/// <summary>Creation rules shared by the folder endpoint and template apply (issue #234).</summary>
internal static class FolderWriter
{
    public static string? NormalizeName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > WorkspaceFolder.NameMaxLength
            ? null
            : trimmed;
    }

    /// <summary>
    /// Adds one folder to the change tracker after checking every rule a folder must satisfy.
    /// Template apply reuses this rather than inserting rows of its own — a bulk path with its
    /// own copy of the depth and uniqueness checks is a bulk path that will eventually skip one.
    /// The caller saves.
    /// </summary>
    public static async Task<Result<WorkspaceFolder>> CreateAsync(
        AppDbContext db,
        Guid workspaceId,
        Guid createdById,
        string? name,
        Guid? parentId,
        WorkspaceFolder.EffectiveDefaults? defaults,
        CancellationToken ct)
    {
        var normalized = NormalizeName(name);
        if (normalized is null)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNameInvalid));

        if (parentId is { } id)
        {
            var parent = await db.WorkspaceFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == id && f.WorkspaceId == workspaceId, ct);

            if (parent is null)
                return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNotFound));

            // A parent that already has a parent would make this a grandchild.
            if (parent.ParentId is not null)
                return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderTooDeep));
        }

        var count = await db.WorkspaceFolders.CountAsync(f => f.WorkspaceId == workspaceId, ct);
        if (count >= WorkspaceFolder.MaxPerWorkspace)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderLimitReached));

        var taken = await db.WorkspaceFolders.AnyAsync(
            f => f.WorkspaceId == workspaceId && f.ParentId == parentId && f.Name == normalized, ct);
        if (taken)
            return Result.Failure<WorkspaceFolder>(new LightResults.Error(ErrorCodes.FolderNameTaken));

        var folder = new WorkspaceFolder
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspaceId,
            ParentId = parentId,
            Name = normalized,
            CreatedById = createdById,
            CreatedAt = DateTime.UtcNow,
            DefaultExpiryHours = defaults?.ExpiryHours,
            DefaultPasswordRequired = defaults?.PasswordRequired ?? false,
            DefaultPasswordHint = defaults?.PasswordHint,
            DefaultPrivacyMode = defaults?.PrivacyMode ?? false,
            DefaultsLocked = defaults?.Locked ?? false,
        };
        db.WorkspaceFolders.Add(folder);

        return Result.Success(folder);
    }
}

/// <summary>One projection of the folder tree, with the per-folder size the report cannot show.</summary>
internal static class FolderReader
{
    public static async Task<FolderListDto> ListAsync(
        AppDbContext db, Guid workspaceId, bool canManage, CancellationToken ct)
    {
        var folders = await db.WorkspaceFolders
            .AsNoTracking()
            .Where(f => f.WorkspaceId == workspaceId)
            .OrderBy(f => f.ParentId == null ? 0 : 1)
            .ThenBy(f => f.Name)
            .ToListAsync(ct);

        // Live rows only: a folder's size should not count what is sitting in the trash.
        var sizes = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.FolderId != null && d.WorkspaceId == workspaceId && d.DeletedAt == null)
            .GroupBy(d => d.FolderId!.Value)
            .Select(g => new { FolderId = g.Key, Count = g.Count(), Bytes = g.Sum(d => d.FileSize) })
            .ToListAsync(ct);

        var byFolder = sizes.ToDictionary(s => s.FolderId);

        return new FolderListDto(
            folders.Select(f => byFolder.TryGetValue(f.Id, out var size)
                ? ToDto(f, size.Count, size.Bytes)
                : ToDto(f, 0, 0)).ToList(),
            canManage);
    }

    public static FolderDto ToDto(WorkspaceFolder folder, int deliveries, long bytes) => new(
        folder.Id,
        folder.ParentId,
        folder.Name,
        deliveries,
        bytes,
        folder.DefaultExpiryHours,
        folder.DefaultPasswordRequired,
        folder.DefaultPasswordHint,
        folder.DefaultPrivacyMode,
        folder.DefaultsLocked);
}
