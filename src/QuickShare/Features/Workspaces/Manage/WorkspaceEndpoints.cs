using LightResults;

namespace QuickShare.Features.Workspaces.Manage;

/// <summary>
/// Maps workspace error codes onto HTTP status. Kept in one place so every workspace
/// endpoint answers the same way — a 403 from one route and a 400 from the next for the
/// same refusal is how clients grow bespoke error handling per endpoint.
/// </summary>
internal static class WorkspaceStatus
{
    public static int For(string code) => code switch
    {
        ErrorCodes.WorkspaceNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.WorkspaceMemberNotFound => StatusCodes.Status404NotFound,
        ErrorCodes.WorkspaceForbidden => StatusCodes.Status403Forbidden,
        ErrorCodes.RequiresPaidPlan => StatusCodes.Status403Forbidden,
        ErrorCodes.WorkspaceOwnerImmutable => StatusCodes.Status403Forbidden,
        ErrorCodes.WorkspaceAlreadyExists => StatusCodes.Status409Conflict,
        ErrorCodes.WorkspaceAlreadyMember => StatusCodes.Status409Conflict,
        ErrorCodes.WorkspaceSeatsFull => StatusCodes.Status409Conflict,
        ErrorCodes.WorkspaceTransferOpen => StatusCodes.Status409Conflict,
        ErrorCodes.WorkspaceInviteExpired => StatusCodes.Status410Gone,
        _ => StatusCodes.Status400BadRequest,
    };
}

public sealed class CreateWorkspaceRequest
{
    public string? Name { get; set; }
    public int Seats { get; set; } = Workspace.MinSeats;
}

/// <summary>POST /api/workspace — create a team workspace (issue #106).</summary>
public sealed class CreateWorkspaceEndpoint : Endpoint<CreateWorkspaceRequest, WorkspaceDto>
{
    public override void Configure()
    {
        Post("/api/workspace");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Create a team workspace";
            s.Description = "Paid plans only — the creator becomes the owner and the bill payer. Members invited onto seats inherit the owner plan and draw from one pooled monthly quota.";
        });
    }

    public override async Task HandleAsync(CreateWorkspaceRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceDto>>(
            new CreateWorkspaceCommand(req.Name, req.Seats), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

/// <summary>GET /api/workspace — the caller's workspace, seats and pooled usage (issue #106).</summary>
public sealed class GetWorkspaceEndpoint : EndpointWithoutRequest<WorkspaceDto>
{
    public override void Configure()
    {
        Get("/api/workspace");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s => s.Summary = "Get the caller's workspace");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceDto>>(new GetWorkspaceQuery(), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

public sealed class UpdateWorkspaceRequest
{
    public string? Name { get; set; }
    public int? Seats { get; set; }
    /// <summary>Every share waits for an admin (issue #162). Null leaves the current setting.</summary>
    public bool? RequireDeliveryApproval { get; set; }
    /// <summary>Let an admin clear their own share (issue #162). Null leaves the current setting.</summary>
    public bool? AllowSelfApproval { get; set; }
    /// <summary>disabled / recipients / members (issue #289). Null leaves the current setting.</summary>
    public string? CommentPolicy { get; set; }
}

/// <summary>PATCH /api/workspace — rename or resize the seat pool (issue #106).</summary>
public sealed class UpdateWorkspaceEndpoint : Endpoint<UpdateWorkspaceRequest, WorkspaceDto>
{
    public override void Configure()
    {
        Patch("/api/workspace");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Rename the workspace or change its seat count";
            s.Description = "Admins only. Seats cannot be cut below the number already claimed by members and outstanding invites.";
        });
    }

    public override async Task HandleAsync(UpdateWorkspaceRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceDto>>(
            new UpdateWorkspaceCommand(
                req.Name, req.Seats, req.RequireDeliveryApproval, req.AllowSelfApproval, req.CommentPolicy), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

public sealed class InviteMemberRequest
{
    public string? Email { get; set; }
    public string? Role { get; set; } = nameof(WorkspaceRole.Member);
}

/// <summary>POST /api/workspace/invites — send a seat invite (issue #106).</summary>
public sealed class InviteMemberEndpoint : Endpoint<InviteMemberRequest, WorkspaceMemberDto>
{
    public override void Configure()
    {
        Post("/api/workspace/invites");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Invite an account onto a seat";
            s.Description = "Admins only. The invite holds the seat while it is outstanding and expires after 7 days.";
        });
    }

    public override async Task HandleAsync(InviteMemberRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceMemberDto>>(
            new InviteMemberCommand(req.Email, req.Role), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

public sealed class AcceptInviteRequest
{
    public string? Token { get; set; }
}

/// <summary>POST /api/workspace/invites/accept — claim a seat with an invite token (issue #106).</summary>
public sealed class AcceptInviteEndpoint : Endpoint<AcceptInviteRequest, WorkspaceDto>
{
    public override void Configure()
    {
        Post("/api/workspace/invites/accept");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Accept a workspace invitation";
            s.Description = "Signed-in accounts only. Accepting moves the account onto the workspace plan; the plan it paid for on its own is restored when the seat is revoked.";
        });
    }

    public override async Task HandleAsync(AcceptInviteRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceDto>>(
            new AcceptInviteCommand(req.Token), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

public sealed class ChangeMemberRoleRequest
{
    public string? Role { get; set; }
}

/// <summary>PATCH /api/workspace/members/{Id} — change a member's role (issue #106).</summary>
public sealed class ChangeMemberRoleEndpoint : Endpoint<ChangeMemberRoleRequest, WorkspaceMemberDto>
{
    public override void Configure()
    {
        Patch("/api/workspace/members/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Change a member's role";
            s.Description = "Admins only. Owner is not assignable here — ownership moves through the transfer flow so the successor can consent.";
        });
    }

    public override async Task HandleAsync(ChangeMemberRoleRequest req, CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceMemberDto>>(
            new ChangeMemberRoleCommand(Route<Guid>("Id"), req.Role), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}

/// <summary>DELETE /api/workspace/members/{Id} — revoke a seat (issue #106).</summary>
public sealed class RevokeMemberEndpoint : EndpointWithoutRequest<WorkspaceDto>
{
    public override void Configure()
    {
        Delete("/api/workspace/members/{Id}");
        Description(b => b.WithTags(ApiTags.Workspaces));
        Summary(s =>
        {
            s.Summary = "Revoke a seat";
            s.Description = "Admins only. Frees the slot immediately, restores the member's own plan, and leaves their workspace deliveries with the workspace.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var result = await Resolve<IMediator>().Send<Result<WorkspaceDto>>(
            new RevokeMemberCommand(Route<Guid>("Id")), ct);

        if (result.IsSuccess(out var response))
            await Send.OkAsync(response, ct);
        else if (result.IsFailure(out var error))
            ThrowError(error.Message, WorkspaceStatus.For(error.Message));
    }
}
