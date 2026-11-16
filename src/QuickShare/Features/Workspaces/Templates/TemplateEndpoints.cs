using LightResults;
using QuickShare.Features.Workspaces.Folders;

namespace QuickShare.Features.Workspaces.Templates;

/// <summary>GET /api/workspace/templates — the workspace's folder templates (issue #234).</summary>
public sealed class ListTemplatesEndpoint : EndpointWithoutRequest<TemplateListDto>
{
    public override void Configure()
    {
        Get("/api/workspace/templates");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "List the workspace's folder templates");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TemplateListDto>>(new ListTemplatesQuery(), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

public sealed class SaveTemplateRequest
{
    public string? Name { get; set; }
    /// <summary>Snapshot this folder and its children; omit to snapshot the whole workspace layout.</summary>
    public Guid? FromFolderId { get; set; }
}

/// <summary>POST /api/workspace/templates — save a folder structure as a template (issue #234).</summary>
public sealed class SaveTemplateEndpoint : Endpoint<SaveTemplateRequest, TemplateDto>
{
    public override void Configure()
    {
        Post("/api/workspace/templates");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Save a folder structure as a reusable template";
            s.Description = "Workspace admins only. The template is a snapshot of names and default settings — it holds no reference to the folders it was taken from.";
        });
    }

    public override async Task HandleAsync(SaveTemplateRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TemplateDto>>(
            new SaveTemplateCommand(req.Name, req.FromFolderId), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

public sealed class ApplyTemplateRequest
{
    /// <summary>Optional parent folder to nest the whole structure under — one client per root.</summary>
    public string? RootName { get; set; }
}

/// <summary>POST /api/workspace/templates/{Id}/apply — create folders from a template (issue #234).</summary>
public sealed class ApplyTemplateEndpoint : Endpoint<ApplyTemplateRequest, ApplyTemplateResultDto>
{
    public override void Configure()
    {
        Post("/api/workspace/templates/{Id}/apply");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Create folders from a template";
            s.Description = "Workspace admins only. Idempotent by name: folders that already exist are reported in `skipped`, so an apply can safely be re-run.";
        });
    }

    public override async Task HandleAsync(ApplyTemplateRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<ApplyTemplateResultDto>>(
            new ApplyTemplateCommand(Route<Guid>("Id"), req.RootName), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}

/// <summary>DELETE /api/workspace/templates/{Id} — delete a template (issue #234).</summary>
public sealed class DeleteTemplateEndpoint : EndpointWithoutRequest<TemplateListDto>
{
    public override void Configure()
    {
        Delete("/api/workspace/templates/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Delete a folder template";
            s.Description = "Workspace admins only. Folders already created from the template are untouched.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<TemplateListDto>>(
            new DeleteTemplateCommand(Route<Guid>("Id")), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, FolderStatus.For(error.Message));
    }
}
