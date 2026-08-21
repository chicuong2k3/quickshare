using System.Text.Json;
using LightResults;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Folders;
using QuickShare.Features.Workspaces.Shared;

namespace QuickShare.Features.Workspaces.Templates;

/// <summary>
/// One folder in a saved structure (issue #234). A value type on purpose: a template is a
/// snapshot, so it carries names and settings, never ids pointing at live folders.
/// </summary>
public sealed record FolderTemplateNode(
    string Name,
    double? ExpiryHours,
    bool PasswordRequired,
    string? PasswordHint,
    bool PrivacyMode,
    IReadOnlyList<FolderTemplateNode>? Children);

public sealed record TemplateDto(Guid Id, string Name, int FolderCount, DateTime CreatedAt);
public sealed record TemplateListDto(IReadOnlyList<TemplateDto> Templates, bool CanManage);
public sealed record ApplyTemplateResultDto(int Created, int Skipped, IReadOnlyList<FolderDto> Folders);

public record ListTemplatesQuery : IRequest<Result<TemplateListDto>>;
public record SaveTemplateCommand(string? Name, Guid? FromFolderId) : IRequest<Result<TemplateDto>>;
public record ApplyTemplateCommand(Guid TemplateId, string? RootName) : IRequest<Result<ApplyTemplateResultDto>>;
public record DeleteTemplateCommand(Guid TemplateId) : IRequest<Result<TemplateListDto>>;

public sealed class ListTemplatesHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<ListTemplatesQuery, Result<TemplateListDto>>
{
    public async ValueTask<Result<TemplateListDto>> Handle(ListTemplatesQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<TemplateListDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));

        return Result.Success(await TemplateReader.ListAsync(db, seat.WorkspaceId, seat.CanAdminister, cancellationToken));
    }
}

/// <summary>
/// Saves a folder structure as a template (issue #234). With a source folder, the snapshot is
/// that folder plus its children; without one, it is every root folder and their children —
/// "save this whole workspace layout", which is the case an agency actually starts from.
/// </summary>
public sealed class SaveTemplateHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<SaveTemplateCommand, Result<TemplateDto>>
{
    public async ValueTask<Result<TemplateDto>> Handle(SaveTemplateCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var name = Normalize(cmd.Name);
        if (name is null)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.TemplateNameInvalid));

        var taken = await db.WorkspaceFolderTemplates
            .AnyAsync(t => t.WorkspaceId == seat.WorkspaceId && t.Name == name, cancellationToken);
        if (taken)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.TemplateNameTaken));

        var folders = await db.WorkspaceFolders
            .AsNoTracking()
            .Where(f => f.WorkspaceId == seat.WorkspaceId)
            .ToListAsync(cancellationToken);

        var roots = cmd.FromFolderId is { } sourceId
            ? folders.Where(f => f.Id == sourceId).ToList()
            : folders.Where(f => f.ParentId is null).ToList();

        if (roots.Count == 0)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.FolderNotFound));

        var nodes = roots.Select(root => ToNode(root, folders)).ToList();
        if (CountNodes(nodes) > WorkspaceFolderTemplate.MaxFolders)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.TemplateStructureInvalid));

        var json = JsonSerializer.Serialize(nodes);
        if (json.Length > WorkspaceFolderTemplate.StructureMaxLength)
            return Result.Failure<TemplateDto>(new LightResults.Error(ErrorCodes.TemplateStructureInvalid));

        var template = new WorkspaceFolderTemplate
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = seat.WorkspaceId,
            Name = name,
            StructureJson = json,
            CreatedById = user.Id,
            CreatedAt = DateTime.UtcNow,
        };
        db.WorkspaceFolderTemplates.Add(template);
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceTemplateSaved,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { template.Name }));

        return Result.Success(new TemplateDto(template.Id, template.Name, CountNodes(nodes), template.CreatedAt));
    }

    private static FolderTemplateNode ToNode(WorkspaceFolder folder, List<WorkspaceFolder> all)
    {
        // Depth is capped at two, so a child never recurses further — no cycle guard needed.
        var children = all
            .Where(f => f.ParentId == folder.Id)
            .OrderBy(f => f.Name)
            .Select(f => new FolderTemplateNode(
                f.Name, f.DefaultExpiryHours, f.DefaultPasswordRequired,
                f.DefaultPasswordHint, f.DefaultPrivacyMode, Children: null))
            .ToList();

        return new FolderTemplateNode(
            folder.Name,
            folder.DefaultExpiryHours,
            folder.DefaultPasswordRequired,
            folder.DefaultPasswordHint,
            folder.DefaultPrivacyMode,
            children.Count == 0 ? null : children);
    }

    internal static int CountNodes(IReadOnlyList<FolderTemplateNode> nodes) =>
        nodes.Sum(n => 1 + (n.Children?.Count ?? 0));

    internal static string? Normalize(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > WorkspaceFolderTemplate.NameMaxLength
            ? null
            : trimmed;
    }
}

/// <summary>
/// Creates folders from a template (issue #234) — the one-click client onboarding.
///
/// Two decisions worth stating. Apply is **idempotent by name**: a folder that already exists
/// is skipped and counted, not failed, so a half-finished apply can be re-run and a second
/// project can reuse the same names under a different root. And the template is **read, never
/// linked**: nothing the apply creates points back at it, so editing or deleting the template
/// afterwards cannot reach into folders already in use.
///
/// An optional root name nests the whole structure one level down, which is how the same
/// "Client Project" template serves every client without collisions. Because nesting caps at
/// two levels, a template that has children of its own is applied flat under that root, and
/// the skipped count says so rather than the apply half-failing.
/// </summary>
public sealed class ApplyTemplateHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<ApplyTemplateCommand, Result<ApplyTemplateResultDto>>
{
    public async ValueTask<Result<ApplyTemplateResultDto>> Handle(ApplyTemplateCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<ApplyTemplateResultDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<ApplyTemplateResultDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var template = await db.WorkspaceFolderTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == cmd.TemplateId && t.WorkspaceId == seat.WorkspaceId, cancellationToken);
        if (template is null)
            return Result.Failure<ApplyTemplateResultDto>(new LightResults.Error(ErrorCodes.TemplateNotFound));

        List<FolderTemplateNode>? nodes;
        try
        {
            nodes = JsonSerializer.Deserialize<List<FolderTemplateNode>>(template.StructureJson);
        }
        catch (JsonException)
        {
            nodes = null;
        }

        if (nodes is null || nodes.Count == 0)
            return Result.Failure<ApplyTemplateResultDto>(new LightResults.Error(ErrorCodes.TemplateStructureInvalid));

        var created = 0;
        var skipped = 0;

        Guid? rootId = null;
        var rootName = SaveTemplateHandler.Normalize(cmd.RootName);
        if (rootName is not null)
        {
            var root = await FolderWriter.CreateAsync(
                db, seat.WorkspaceId, user.Id, rootName, parentId: null, defaults: null, cancellationToken);

            if (root.IsFailure(out var rootError))
            {
                // An existing root is reuse, not failure — that is what makes apply re-runnable.
                if (rootError.Message != ErrorCodes.FolderNameTaken)
                    return Result.Failure<ApplyTemplateResultDto>(rootError);

                rootId = await db.WorkspaceFolders
                    .Where(f => f.WorkspaceId == seat.WorkspaceId && f.ParentId == null && f.Name == rootName)
                    .Select(f => (Guid?)f.Id)
                    .FirstOrDefaultAsync(cancellationToken);
                skipped++;
            }
            else
            {
                root.IsSuccess(out var rootFolder);
                rootId = rootFolder!.Id;
                created++;
                // Saved now so the children below can reference it as a parent.
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        foreach (var node in nodes)
        {
            var outcome = await CreateNodeAsync(db, seat.WorkspaceId, user.Id, node, rootId, cancellationToken);
            if (outcome.IsFailure(out var error))
            {
                if (error.Message is ErrorCodes.FolderNameTaken or ErrorCodes.FolderTooDeep)
                {
                    skipped++;
                    continue;
                }

                // A hard stop (workspace folder cap, malformed name) ends the apply rather than
                // leaving the caller guessing which half of the structure exists.
                return Result.Failure<ApplyTemplateResultDto>(error);
            }

            created++;
            await db.SaveChangesAsync(cancellationToken);

            outcome.IsSuccess(out var parent);
            foreach (var child in node.Children ?? [])
            {
                var childOutcome = await CreateNodeAsync(
                    db, seat.WorkspaceId, user.Id, child, parent!.Id, cancellationToken);

                if (childOutcome.IsFailure(out var childError))
                {
                    if (childError.Message is ErrorCodes.FolderNameTaken or ErrorCodes.FolderTooDeep)
                    {
                        skipped++;
                        continue;
                    }

                    return Result.Failure<ApplyTemplateResultDto>(childError);
                }

                created++;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceTemplateApplied,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { template.Name, created, skipped }));

        var list = await FolderReader.ListAsync(db, seat.WorkspaceId, canManage: true, cancellationToken);
        return Result.Success(new ApplyTemplateResultDto(created, skipped, list.Folders));
    }

    private static Task<Result<WorkspaceFolder>> CreateNodeAsync(
        AppDbContext db,
        Guid workspaceId,
        Guid userId,
        FolderTemplateNode node,
        Guid? parentId,
        CancellationToken ct)
        => FolderWriter.CreateAsync(db, workspaceId, userId, node.Name, parentId,
            new WorkspaceFolder.EffectiveDefaults(
                node.ExpiryHours, node.PasswordRequired, node.PasswordHint, node.PrivacyMode, Locked: false),
            ct);
}

/// <summary>
/// Deletes a template (issue #234). Folders created from it are untouched — they are real
/// folders holding real files, and the template was only ever the mould.
/// </summary>
public sealed class DeleteTemplateHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<DeleteTemplateCommand, Result<TemplateListDto>>
{
    public async ValueTask<Result<TemplateListDto>> Handle(DeleteTemplateCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<TemplateListDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<TemplateListDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var template = await db.WorkspaceFolderTemplates
            .FirstOrDefaultAsync(t => t.Id == cmd.TemplateId && t.WorkspaceId == seat.WorkspaceId, cancellationToken);
        if (template is null)
            return Result.Failure<TemplateListDto>(new LightResults.Error(ErrorCodes.TemplateNotFound));

        db.WorkspaceFolderTemplates.Remove(template);
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceTemplateDeleted,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { template.Name }));

        return Result.Success(await TemplateReader.ListAsync(db, seat.WorkspaceId, canManage: true, cancellationToken));
    }
}

internal static class TemplateReader
{
    public static async Task<TemplateListDto> ListAsync(
        AppDbContext db, Guid workspaceId, bool canManage, CancellationToken ct)
    {
        var rows = await db.WorkspaceFolderTemplates
            .AsNoTracking()
            .Where(t => t.WorkspaceId == workspaceId)
            .OrderBy(t => t.Name)
            .Select(t => new { t.Id, t.Name, t.StructureJson, t.CreatedAt })
            .ToListAsync(ct);

        return new TemplateListDto(
            rows.Select(t => new TemplateDto(t.Id, t.Name, CountFolders(t.StructureJson), t.CreatedAt)).ToList(),
            canManage);
    }

    /// <summary>A stored blob that no longer parses counts as zero folders rather than throwing a list request.</summary>
    private static int CountFolders(string json)
    {
        try
        {
            var nodes = JsonSerializer.Deserialize<List<FolderTemplateNode>>(json);
            return nodes is null ? 0 : SaveTemplateHandler.CountNodes(nodes);
        }
        catch (JsonException)
        {
            return 0;
        }
    }
}
