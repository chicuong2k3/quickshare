using System.Text.Json;
using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Workspaces.Manage;

public sealed record WorkspaceMemberDto(
    Guid Id,
    Guid? UserId,
    string Email,
    string Name,
    string Role,
    bool Pending,
    DateTime? JoinedAt);

public sealed record WorkspaceDto(
    Guid Id,
    string Name,
    string Plan,
    int Seats,
    int SeatsUsed,
    string Role,
    bool IsOwner,
    long PooledQuotaBytes,
    long PooledUsedBytes,
    IReadOnlyList<WorkspaceMemberDto> Members);

public record CreateWorkspaceCommand(string? Name, int Seats) : IRequest<Result<WorkspaceDto>>;
public record GetWorkspaceQuery : IRequest<Result<WorkspaceDto>>;
public record UpdateWorkspaceCommand(string? Name, int? Seats) : IRequest<Result<WorkspaceDto>>;
public record InviteMemberCommand(string? Email, string? Role) : IRequest<Result<WorkspaceMemberDto>>;
public record AcceptInviteCommand(string? Token) : IRequest<Result<WorkspaceDto>>;
public record ChangeMemberRoleCommand(Guid MemberId, string? Role) : IRequest<Result<WorkspaceMemberDto>>;
public record RevokeMemberCommand(Guid MemberId) : IRequest<Result<WorkspaceDto>>;

/// <summary>
/// Creates a workspace (issue #106). The creator becomes the Owner seat, which is a real
/// member row rather than a special case — every seat count, audit row and transfer step
/// then reads one table instead of remembering that the owner lives somewhere else.
///
/// One workspace per owner and one live seat per account: overlapping seats would give
/// <see cref="User.Plan"/> two masters, and the loser would be a silently wrong tier gate.
/// </summary>
public sealed class CreateWorkspaceHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<CreateWorkspaceCommand, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(CreateWorkspaceCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        if (!WorkspaceService.CanOwnWorkspace(user.Plan))
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var name = cmd.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > Workspace.NameMaxLength)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceNameInvalid));

        if (cmd.Seats < Workspace.MinSeats || cmd.Seats > Workspace.MaxSeats)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceSeatsInvalid));

        var alreadySeated = await db.WorkspaceMembers
            .AnyAsync(m => m.UserId == user.Id && m.RevokedAt == null, cancellationToken);
        if (alreadySeated)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceAlreadyExists));

        var now = DateTime.UtcNow;
        var workspace = new Workspace
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            OwnerId = user.Id,
            Seats = cmd.Seats,
            CreatedAt = now,
        };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = workspace.Id,
            UserId = user.Id,
            InviteEmail = user.Email,
            Role = WorkspaceRole.Owner,
            JoinedAt = now,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(workspace.Id, user.Id, SecurityEventType.WorkspaceCreated,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { workspace.Name, workspace.Seats }));

        return Result.Success(await WorkspaceReader.LoadAsync(db, workspace.Id, user.Id, cancellationToken));
    }
}

public sealed class GetWorkspaceHandler(AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<GetWorkspaceQuery, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(GetWorkspaceQuery query, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var workspaceId = await db.WorkspaceMembers
            .Where(m => m.UserId == user.Id && m.RevokedAt == null && m.JoinedAt != null)
            .Select(m => (Guid?)m.WorkspaceId)
            .FirstOrDefaultAsync(cancellationToken);

        if (workspaceId is null)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));

        return Result.Success(await WorkspaceReader.LoadAsync(db, workspaceId.Value, user.Id, cancellationToken));
    }
}

/// <summary>
/// Renames the workspace or resizes the seat pool (issue #106). Seats may never be cut
/// below what is already claimed — silently evicting whoever sorted last is not a setting.
/// </summary>
public sealed class UpdateWorkspaceHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<UpdateWorkspaceCommand, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(UpdateWorkspaceCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var workspace = seat.Workspace;

        if (cmd.Name is not null)
        {
            var name = cmd.Name.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > Workspace.NameMaxLength)
                return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceNameInvalid));
            workspace.Name = name;
        }

        if (cmd.Seats is { } seats)
        {
            if (seats < Workspace.MinSeats || seats > Workspace.MaxSeats)
                return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceSeatsInvalid));

            var held = await workspaces.CountHeldSeatsAsync(workspace.Id, cancellationToken);
            if (seats < held)
                return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceSeatsFull));

            workspace.Seats = seats;
        }

        workspace.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(workspace.Id, user.Id,
            cmd.Seats is null ? SecurityEventType.WorkspaceRenamed : SecurityEventType.WorkspaceSeatsChanged,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { workspace.Name, workspace.Seats }));

        return Result.Success(await WorkspaceReader.LoadAsync(db, workspace.Id, user.Id, cancellationToken));
    }
}

/// <summary>
/// Invites an account onto a seat (issue #106). The invite row claims the seat immediately,
/// so two admins racing for the last slot cannot both win; the token is emailed and only its
/// SHA-256 is stored, and it dies after seven days.
/// </summary>
public sealed class InviteMemberHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<InviteMemberHandler> logger)
    : IRequestHandler<InviteMemberCommand, Result<WorkspaceMemberDto>>
{
    private const string Subject = "You have been invited to a QuickShare workspace";

    public async ValueTask<Result<WorkspaceMemberDto>> Handle(InviteMemberCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var email = cmd.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || !email.Contains('@') || email.Length > WorkspaceMember.EmailMaxLength)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceEmailInvalid));

        if (!TryParseRole(cmd.Role, out var role))
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.BadRequest));
        if (role == WorkspaceRole.Owner)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceOwnerImmutable));

        var duplicate = await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == seat.WorkspaceId && m.RevokedAt == null && m.InviteEmail == email, cancellationToken);
        if (duplicate)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceAlreadyMember));

        var held = await workspaces.CountHeldSeatsAsync(seat.WorkspaceId, cancellationToken);
        if (held >= seat.Workspace.Seats)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceSeatsFull));

        var token = ShareCodes.Generate();
        var now = DateTime.UtcNow;
        var member = new WorkspaceMember
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = seat.WorkspaceId,
            InviteEmail = email,
            Role = role,
            InviteTokenHash = WorkspaceService.HashToken(token),
            InviteExpiresAt = now.AddDays(WorkspaceMember.InviteValidDays),
            CreatedAt = now,
        };
        db.WorkspaceMembers.Add(member);
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceMemberInvited,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { email, role = role.ToString() }));

        await SendInviteAsync(email, seat.Workspace.Name, token, cancellationToken);

        return Result.Success(ToDto(member, name: string.Empty));
    }

    private async Task SendInviteAsync(string email, string workspaceName, string token, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
            return;

        var baseUrl = emailOptions.Value.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}{RoutePaths.WorkspaceJoin}?token={token}";
        var body = $"""
            <p>You have been invited to join the <strong>{System.Net.WebUtility.HtmlEncode(workspaceName)}</strong> workspace on QuickShare.</p>
            <p><a href="{link}">Accept the invitation</a></p>
            <p>This invitation expires in {WorkspaceMember.InviteValidDays} days.</p>
            """;

        try
        {
            await emailSender.SendAsync(email, "QuickShare", Subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace invite email failed for {Email}", email);
        }
    }

    internal static bool TryParseRole(string? value, out WorkspaceRole role)
        => Enum.TryParse(value, ignoreCase: true, out role);

    internal static WorkspaceMemberDto ToDto(WorkspaceMember member, string name) => new(
        member.Id,
        member.UserId,
        member.InviteEmail,
        name,
        member.Role.ToString().ToLowerInvariant(),
        member.IsPending,
        member.JoinedAt);
}

/// <summary>
/// Claims a seat from an invite token (issue #106). The signed-in account's email does not
/// have to match the invited address — the token is the capability, and forcing a match
/// would lock out anyone whose work address differs from their sign-in one.
/// </summary>
public sealed class AcceptInviteHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<AcceptInviteCommand, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(AcceptInviteCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        if (string.IsNullOrWhiteSpace(cmd.Token))
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceInviteInvalid));

        var hash = WorkspaceService.HashToken(cmd.Token.Trim());
        var member = await db.WorkspaceMembers
            .Include(m => m.Workspace)
            .FirstOrDefaultAsync(m => m.InviteTokenHash == hash && m.RevokedAt == null, cancellationToken);

        if (member is null || member.JoinedAt is not null)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceInviteInvalid));

        if (member.InviteExpiresAt is { } expires && expires <= DateTime.UtcNow)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceInviteExpired));

        var alreadySeated = await db.WorkspaceMembers
            .AnyAsync(m => m.UserId == user.Id && m.RevokedAt == null, cancellationToken);
        if (alreadySeated)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceAlreadyMember));

        var now = DateTime.UtcNow;
        member.UserId = user.Id;
        member.JoinedAt = now;
        member.InviteTokenHash = null;
        member.InviteExpiresAt = null;
        member.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.SyncMemberPlansAsync(member.WorkspaceId, cancellationToken);
        await workspaces.AuditAsync(member.WorkspaceId, user.Id, SecurityEventType.WorkspaceMemberJoined,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { role = member.Role.ToString() }));

        return Result.Success(await WorkspaceReader.LoadAsync(db, member.WorkspaceId, user.Id, cancellationToken));
    }
}

/// <summary>Changes a member's role (issue #106). Owner is not a role you can hand out — #252 moves it.</summary>
public sealed class ChangeMemberRoleHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<ChangeMemberRoleCommand, Result<WorkspaceMemberDto>>
{
    public async ValueTask<Result<WorkspaceMemberDto>> Handle(ChangeMemberRoleCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        if (!InviteMemberHandler.TryParseRole(cmd.Role, out var role))
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.BadRequest));
        if (role == WorkspaceRole.Owner)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceOwnerImmutable));

        var member = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.Id == cmd.MemberId && m.WorkspaceId == seat.WorkspaceId && m.RevokedAt == null, cancellationToken);
        if (member is null)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceMemberNotFound));
        if (member.Role == WorkspaceRole.Owner)
            return Result.Failure<WorkspaceMemberDto>(new LightResults.Error(ErrorCodes.WorkspaceOwnerImmutable));

        member.Role = role;
        member.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceMemberRoleChanged,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { member.InviteEmail, role = role.ToString() }));

        return Result.Success(InviteMemberHandler.ToDto(member, name: string.Empty));
    }
}

/// <summary>
/// Revokes a seat (issue #106) — the freed slot is immediately invitable again. The row is
/// kept, not deleted, so the audit log keeps its subject; the member's own plan is restored
/// rather than dropped to Free, and their workspace deliveries stay with the workspace.
/// </summary>
public sealed class RevokeMemberHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<RevokeMemberCommand, Result<WorkspaceDto>>
{
    public async ValueTask<Result<WorkspaceDto>> Handle(RevokeMemberCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (!seat.CanAdminister)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var member = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.Id == cmd.MemberId && m.WorkspaceId == seat.WorkspaceId && m.RevokedAt == null, cancellationToken);
        if (member is null)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceMemberNotFound));
        if (member.Role == WorkspaceRole.Owner)
            return Result.Failure<WorkspaceDto>(new LightResults.Error(ErrorCodes.WorkspaceOwnerImmutable));

        var now = DateTime.UtcNow;
        member.RevokedAt = now;
        member.InviteTokenHash = null;
        member.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        if (member.UserId is { } revokedUserId)
            await workspaces.RestorePersonalPlanAsync(revokedUserId, cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceMemberRevoked,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { member.InviteEmail }));

        return Result.Success(await WorkspaceReader.LoadAsync(db, seat.WorkspaceId, user.Id, cancellationToken));
    }
}

/// <summary>
/// One projection of a workspace, shared by every handler that returns one. Pooled usage is
/// read from the owner's monthly usage row, because that is the bucket every seat writes to.
/// </summary>
internal static class WorkspaceReader
{
    public static async Task<WorkspaceDto> LoadAsync(AppDbContext db, Guid workspaceId, Guid viewerId, CancellationToken cancellationToken)
    {
        var workspace = await db.Workspaces
            .AsNoTracking()
            .Include(w => w.Owner)
            .FirstAsync(w => w.Id == workspaceId, cancellationToken);

        var members = await db.WorkspaceMembers
            .AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.RevokedAt == null)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.CreatedAt)
            .Select(m => new
            {
                m.Id,
                m.UserId,
                m.InviteEmail,
                m.Role,
                m.JoinedAt,
                Name = db.Users.Where(u => u.Id == m.UserId).Select(u => u.Name).FirstOrDefault() ?? string.Empty,
            })
            .ToListAsync(cancellationToken);

        var periodStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var used = await db.UserUsages
            .AsNoTracking()
            .Where(u => u.UserId == workspace.OwnerId && u.PeriodStart == periodStart)
            .Select(u => (long?)u.TotalBytesUploaded)
            .FirstOrDefaultAsync(cancellationToken) ?? 0;

        var quota = (PlanTierLimits.GetMonthlyQuota(workspace.Owner.Plan) ?? 0) + workspace.Owner.BonusQuotaBytes;

        var viewerRole = members.FirstOrDefault(m => m.UserId == viewerId)?.Role ?? WorkspaceRole.Viewer;

        return new WorkspaceDto(
            workspace.Id,
            workspace.Name,
            workspace.Owner.Plan,
            workspace.Seats,
            members.Count,
            viewerRole.ToString().ToLowerInvariant(),
            workspace.OwnerId == viewerId,
            quota,
            used,
            members.Select(m => new WorkspaceMemberDto(
                m.Id,
                m.UserId,
                m.InviteEmail,
                m.Name,
                m.Role.ToString().ToLowerInvariant(),
                m.JoinedAt is null,
                m.JoinedAt)).ToList());
    }
}
