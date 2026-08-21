using System.Text.Json;
using LightResults;
using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Workspaces.Transfer;

public sealed record TransferDto(
    Guid Id,
    Guid ToUserId,
    string ToEmail,
    DateTime ExpiresAt,
    DateTime? AcceptedAt);

public record InitiateTransferCommand(Guid MemberId) : IRequest<Result<TransferDto>>;
public record AcceptTransferCommand(string? Token) : IRequest<Result<TransferDto>>;
public record CancelTransferCommand : IRequest<Result<TransferDto>>;

/// <summary>
/// Opens an ownership handover (issue #252). The owner nominates an active member; nothing
/// moves yet. Ownership carries the bill with it, so the successor has to accept inside a
/// 7-day window — a workspace whose owner account disappears is the failure this prevents,
/// but taking on someone's invoice without saying yes is not an acceptable cure.
///
/// One open transfer at a time: two live tokens would race, and whichever landed second
/// would demote an owner who had already been demoted.
/// </summary>
public sealed class InitiateTransferHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces,
    EmailSender emailSender,
    IOptions<EmailOptions> emailOptions,
    ILogger<InitiateTransferHandler> logger)
    : IRequestHandler<InitiateTransferCommand, Result<TransferDto>>
{
    private const string Subject = "You have been nominated to own a QuickShare workspace";

    public async ValueTask<Result<TransferDto>> Handle(InitiateTransferCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));

        // Only the owner may give ownership away — an admin handing off someone else's
        // bill is the escalation this guard exists to refuse.
        if (seat.Role != WorkspaceRole.Owner)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var successor = await db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.Id == cmd.MemberId
                && m.WorkspaceId == seat.WorkspaceId
                && m.RevokedAt == null
                && m.JoinedAt != null, cancellationToken);

        if (successor?.UserId is null)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceMemberNotFound));

        if (successor.UserId == user.Id)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferSelf));

        var now = DateTime.UtcNow;
        var alreadyOpen = await db.WorkspaceTransfers
            .AnyAsync(t => t.WorkspaceId == seat.WorkspaceId
                && t.AcceptedAt == null
                && t.CancelledAt == null
                && t.ExpiresAt > now, cancellationToken);
        if (alreadyOpen)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferOpen));

        var token = ShareCodes.Generate();
        var transfer = new WorkspaceTransfer
        {
            Id = Guid.CreateVersion7(),
            WorkspaceId = seat.WorkspaceId,
            FromUserId = user.Id,
            ToUserId = successor.UserId.Value,
            TokenHash = WorkspaceService.HashToken(token),
            ExpiresAt = now.AddDays(WorkspaceTransfer.ValidDays),
            CreatedAt = now,
        };
        db.WorkspaceTransfers.Add(transfer);
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceTransferInitiated,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { to = successor.InviteEmail }));

        await SendAsync(successor.InviteEmail, seat.Workspace.Name, token, cancellationToken);

        return Result.Success(new TransferDto(
            transfer.Id, transfer.ToUserId, successor.InviteEmail, transfer.ExpiresAt, null));
    }

    private async Task SendAsync(string email, string workspaceName, string token, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured)
            return;

        var baseUrl = emailOptions.Value.BaseUrl.TrimEnd('/');
        var link = $"{baseUrl}{RoutePaths.WorkspaceJoin}?transfer={token}";
        var body = $"""
            <p>You have been nominated as the new owner of the <strong>{System.Net.WebUtility.HtmlEncode(workspaceName)}</strong> workspace on QuickShare.</p>
            <p>Accepting makes you the workspace bill payer.</p>
            <p><a href="{link}">Review and accept</a></p>
            <p>This nomination expires in {WorkspaceTransfer.ValidDays} days.</p>
            """;

        try
        {
            await emailSender.SendAsync(email, "QuickShare", Subject, body, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Workspace transfer email failed for {Email}", email);
        }
    }
}

/// <summary>
/// Completes the handover (issue #252). The successor becomes Owner, the source owner is
/// demoted to Admin rather than removed — an ex-owner locked out of the workspace they
/// built is worse than one who keeps admin rights they can be stripped of afterwards, and
/// it guarantees the workspace is never left without someone who can administer it.
///
/// Assets do not move: deliveries, audit history and pooled usage belong to the workspace,
/// not to whoever owns it this week. The new owner's plan becomes the workspace plan, which
/// is re-pushed onto every seat here.
/// </summary>
public sealed class AcceptTransferHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<AcceptTransferCommand, Result<TransferDto>>
{
    public async ValueTask<Result<TransferDto>> Handle(AcceptTransferCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        if (string.IsNullOrWhiteSpace(cmd.Token))
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferInvalid));

        var hash = WorkspaceService.HashToken(cmd.Token.Trim());
        var transfer = await db.WorkspaceTransfers
            .Include(t => t.Workspace)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        var now = DateTime.UtcNow;
        if (transfer is null || !transfer.IsOpen(now) || transfer.ToUserId != user.Id)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferInvalid));

        // The successor pays the bill from here on — refuse the handover rather than park a
        // workspace on an account that cannot carry its plan. Their *own* plan is what
        // counts: the seat plan they are wearing right now is the one about to disappear.
        if (!WorkspaceService.CanOwnWorkspace(user.PersonalPlan ?? user.Plan))
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.RequiresPaidPlan));

        var seats = await db.WorkspaceMembers
            .Where(m => m.WorkspaceId == transfer.WorkspaceId && m.RevokedAt == null)
            .ToListAsync(cancellationToken);

        var successorSeat = seats.FirstOrDefault(m => m.UserId == user.Id && m.JoinedAt != null);
        var sourceSeat = seats.FirstOrDefault(m => m.UserId == transfer.FromUserId);
        if (successorSeat is null || sourceSeat is null)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferInvalid));

        successorSeat.Role = WorkspaceRole.Owner;
        successorSeat.UpdatedAt = now;
        sourceSeat.Role = WorkspaceRole.Admin;
        sourceSeat.UpdatedAt = now;

        transfer.Workspace.OwnerId = user.Id;
        transfer.Workspace.UpdatedAt = now;
        transfer.AcceptedAt = now;
        transfer.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        // The new owner pays with their own plan — clear the seat override they were under
        // before re-pushing the (now possibly different) workspace plan to everyone else.
        await workspaces.RestorePersonalPlanAsync(user.Id, cancellationToken);
        await workspaces.SyncMemberPlansAsync(transfer.WorkspaceId, cancellationToken);

        await workspaces.AuditAsync(transfer.WorkspaceId, user.Id, SecurityEventType.WorkspaceTransferAccepted,
            http.HttpContext, cancellationToken, JsonSerializer.Serialize(new { from = transfer.FromUserId }));

        return Result.Success(new TransferDto(
            transfer.Id, transfer.ToUserId, successorSeat.InviteEmail, transfer.ExpiresAt, transfer.AcceptedAt));
    }
}

/// <summary>Withdraws an open nomination (issue #252). Owner only, and only while it is open.</summary>
public sealed class CancelTransferHandler(
    AppDbContext db,
    IHttpContextAccessor http,
    WorkspaceService workspaces)
    : IRequestHandler<CancelTransferCommand, Result<TransferDto>>
{
    public async ValueTask<Result<TransferDto>> Handle(CancelTransferCommand cmd, CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);
        var seat = await workspaces.GetSeatAsync(user.Id, cancellationToken);
        if (seat is null)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceNotFound));
        if (seat.Role != WorkspaceRole.Owner)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceForbidden));

        var now = DateTime.UtcNow;
        var transfer = await db.WorkspaceTransfers
            .FirstOrDefaultAsync(t => t.WorkspaceId == seat.WorkspaceId
                && t.AcceptedAt == null
                && t.CancelledAt == null
                && t.ExpiresAt > now, cancellationToken);

        if (transfer is null)
            return Result.Failure<TransferDto>(new LightResults.Error(ErrorCodes.WorkspaceTransferInvalid));

        transfer.CancelledAt = now;
        transfer.TokenHash = string.Empty;
        transfer.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);

        await workspaces.AuditAsync(seat.WorkspaceId, user.Id, SecurityEventType.WorkspaceTransferCancelled,
            http.HttpContext, cancellationToken);

        return Result.Success(new TransferDto(
            transfer.Id, transfer.ToUserId, string.Empty, transfer.ExpiresAt, null));
    }
}
