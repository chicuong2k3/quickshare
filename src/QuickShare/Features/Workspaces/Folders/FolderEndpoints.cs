using LightResults;
using QuickShare.Features.Workspaces.Manage;

namespace QuickShare.Features.Workspaces.Folders;

/// <summary>
/// Folder error codes → HTTP status, extending the workspace map so a folder route and a
/// member route answer the same way for the same class of refusal.
/// </summary>
internal static class FolderStatus
{
    public static int For(string code) => code switch
    {
        ErrorCodes.FolderNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.TemplateNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.FolderNameTaken => StatusCodes.Status409Conflict,
        ErrorCodes.TemplateNameTaken => StatusCodes.Status409Conflict,
        ErrorCodes.FolderNotEmpty => StatusCodes.Status409Conflict,
        ErrorCodes.FolderLimitReached => StatusCodes.Status409Conflict,
        _ => WorkspaceStatus.For(code),
    };
}

/// <summary>GET /api/workspace/folders — the shared folder tree (issue #151).</summary>
public sealed class ListFoldersEndpoint : EndpointWithoutRequest<FolderListDto>
{
    public override void Configure()
    {
        Get("/api/workspace/folders");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "List the workspace's shared folders";
            s.Description = "Any seat. Includes per-folder delivery count and size, and the folder's default upload settings.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderListDto>>(new ListFoldersQuery(), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

public sealed class CreateFolderRequest
{
    public string? Name { get; set; }
    public Guid? ParentId { get; set; }
}

/// <summary>POST /api/workspace/folders — create a shared folder (issue #151).</summary>
public sealed class CreateFolderEndpoint : Endpoint<CreateFolderRequest, FolderDto>
{
    public override void Configure()
    {
        Post("/api/workspace/folders");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Create a shared folder";
            s.Description = "Workspace admins only. Nesting stops at two levels; sibling names must be unique.";
        });
    }

    public override async Task HandleAsync(CreateFolderRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderDto>>(
            new CreateFolderCommand(req.Name, req.ParentId), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

public sealed class RenameFolderRequest
{
    public string? Name { get; set; }
}

/// <summary>PATCH /api/workspace/folders/{Id} — rename a folder (issue #151).</summary>
public sealed class RenameFolderEndpoint : Endpoint<RenameFolderRequest, FolderDto>
{
    public override void Configure()
    {
        Patch("/api/workspace/folders/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Rename a shared folder");
    }

    public override async Task HandleAsync(RenameFolderRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderDto>>(
            new RenameFolderCommand(Route<Guid>("Id"), req.Name), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

/// <summary>DELETE /api/workspace/folders/{Id} — delete an empty folder (issue #151).</summary>
public sealed class DeleteFolderEndpoint : EndpointWithoutRequest<FolderListDto>
{
    public override void Configure()
    {
        Delete("/api/workspace/folders/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Delete a shared folder";
            s.Description = "Workspace admins only. Refused while any delivery or child folder is still filed under it — deleting a folder never deletes files.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderListDto>>(
            new DeleteFolderCommand(Route<Guid>("Id")), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

public sealed class UpdateFolderDefaultsRequest
{
    public double? ExpiryHours { get; set; }
    public bool PasswordRequired { get; set; }
    public string? PasswordHint { get; set; }
    public bool PrivacyMode { get; set; }
    public bool? Locked { get; set; }
}

/// <summary>PUT /api/workspace/folders/{Id}/defaults — per-folder upload defaults (issue #245).</summary>
public sealed class UpdateFolderDefaultsEndpoint : Endpoint<UpdateFolderDefaultsRequest, FolderDto>
{
    public override void Configure()
    {
        Put("/api/workspace/folders/{Id}/defaults");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Set a folder's default upload settings";
            s.Description = "Admins set defaults; only the owner can lock them. Unlocked defaults prefill the upload form and the sender stays in control; locked defaults are enforced at commit.";
        });
    }

    public override async Task HandleAsync(UpdateFolderDefaultsRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderDto>>(
            new UpdateFolderDefaultsCommand(
                Route<Guid>("Id"), req.ExpiryHours, req.PasswordRequired,
                req.PasswordHint, req.PrivacyMode, req.Locked), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

/// <summary>GET /api/workspace/folders/{Id}/prefill — what an upload into this folder starts from (issue #245).</summary>
public sealed class GetFolderPrefillEndpoint : EndpointWithoutRequest<FolderPrefillDto>
{
    public override void Configure()
    {
        Get("/api/workspace/folders/{Id}/prefill");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Resolve a folder's effective upload defaults";
            s.Description = "Any seat. Walks the parent chain: the nearest folder with an opinion wins, and a lock set anywhere above travels down.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<FolderPrefillDto>>(
            new GetFolderPrefillQuery(Route<Guid>("Id")), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}
