using System.Text.Json;
using QuickShare.Extensions;
using QuickShare.Features.Workspaces.Audit;
using QuickShare.Features.Workspaces.Shared;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Deliveries.Approval;

public sealed record PendingApprovalDto(
    Guid Id,
    string FileName,
    long FileSize,
    string SenderName,
    string SenderEmail,
    Guid? FolderId,
    DateTime RequestedAt);

public sealed class DenyRequest
{
    public string? Reason { get; set; }
}

/// <summary>
/// GET /api/workspace/approvals — shares waiting on a reviewer (issue #162).
///
/// Admins only, and scoped to their workspace like every other admin lens. The queue is a
/// query rather than a table: a delivery in <c>PendingApproval</c> *is* the pending item, and a
/// parallel queue table would be a second place for the two to disagree.
/// </summary>
public sealed class ListPendingApprovalsEndpoint : EndpointWithoutRequest<IReadOnlyList<PendingApprovalDto>>
{
    public override void Configure()
    {
        Get("/api/workspace/approvals");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "List shares awaiting approval";
            s.Description = "Workspace admins only. Oldest first — a review queue that surfaces the newest item first leaves the oldest waiting longest.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(Resolve<WorkspaceService>(), db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var pending = await db.Deliveries
            .AsNoTracking()
            .Where(d => d.WorkspaceId == seat.WorkspaceId
                && d.Status == DeliveryStatus.PendingApproval
                && d.DeletedAt == null)
            .OrderBy(d => d.ApprovalRequestedAt)
            .Select(d => new PendingApprovalDto(
                d.Id,
                d.FileName,
                d.FileSize,
                db.Users.Where(u => u.Id == d.SenderId).Select(u => u.Name).FirstOrDefault() ?? string.Empty,
                db.Users.Where(u => u.Id == d.SenderId).Select(u => u.Email).FirstOrDefault() ?? string.Empty,
                d.FolderId,
                d.ApprovalRequestedAt ?? d.CreatedAt))
            .ToListAsync(ct);

        await Send.OkAsync(pending, ct);
    }
}

/// <summary>
/// POST /api/deliveries/{Id}/approve — clear a share for release (issue #162).
///
/// Approving is the moment the outside world first learns the link exists: recipient emails are
/// held at commit and sent here. That ordering is the whole feature — a gate that mails the
/// link and then asks permission is not a gate.
/// </summary>
public sealed class ApproveDeliveryEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/approve");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Approve a pending share";
            s.Description = "Workspace admins only. Activates the link (or leaves it scheduled if a future send time was set) and releases the held recipient emails.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == Route<Guid>("Id") && d.WorkspaceId == seat.WorkspaceId, ct);

        if (delivery is null)
        {
            ThrowError(ErrorCodes.NotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (delivery.Status != DeliveryStatus.PendingApproval)
        {
            ThrowError(ErrorCodes.ApprovalNotPending, StatusCodes.Status409Conflict);
            return;
        }

        // Self-approval is off unless the workspace deliberately turned it on: the control
        // exists because somebody wanted a second pair of eyes, and one's own eyes are not a
        // second pair.
        if (delivery.SenderId == seat.UserId && !seat.Workspace.AllowSelfApproval)
        {
            ThrowError(ErrorCodes.ApprovalSelfNotAllowed, StatusCodes.Status403Forbidden);
            return;
        }

        var now = DateTime.UtcNow;
        delivery.Status = delivery.DeliverAt is { } deliverAt && deliverAt > now
            ? DeliveryStatus.Scheduled
            : DeliveryStatus.Active;
        delivery.ApprovedById = seat.UserId;
        delivery.ApprovedAt = now;
        delivery.DenyReason = null;
        delivery.UpdatedAt = now;

        var recipients = ApprovalNotifier.SplitRecipients(delivery.PendingRecipients);
        delivery.PendingRecipients = null;

        db.Notifications.Add(new Notification
        {
            UserId = delivery.SenderId!.Value,
            Type = NotificationType.ApprovalDecided,
            Title = delivery.FileName,
            LinkUrl = RoutePaths.AccountDeliveries,
        });

        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value, SecurityEventType.DeliveryApproved,
            HttpContext, ct, JsonSerializer.Serialize(new { delivery.ShareCode, delivery.FileName }));

        // Only now does anybody outside hear about it.
        await Resolve<ApprovalNotifier>().SendReleaseAsync(delivery, recipients, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// POST /api/deliveries/{Id}/deny — refuse a share (issue #162).
///
/// A reason is required. "Denied" with no explanation sends the sender back to guess, and the
/// guess is usually to re-upload the same file. The file goes to trash rather than being
/// destroyed, so a wrong call is recoverable within the grace window.
/// </summary>
public sealed class DenyDeliveryEndpoint : Endpoint<DenyRequest>
{
    public override void Configure()
    {
        Post("/api/deliveries/{Id}/deny");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Deny a pending share";
            s.Description = "Workspace admins only. Requires a reason, notifies the sender, and moves the delivery to trash where it can still be recovered.";
        });
    }

    public override async Task HandleAsync(DenyRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var workspaces = Resolve<WorkspaceService>();
        var seat = await WorkspaceAuditReader.ResolveAdminSeatAsync(workspaces, db, HttpContext, ct);

        if (seat is null)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        var reason = req.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            ThrowError(ErrorCodes.DenyReasonRequired, StatusCodes.Status400BadRequest);
            return;
        }

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.Id == Route<Guid>("Id") && d.WorkspaceId == seat.WorkspaceId, ct);

        if (delivery is null)
        {
            ThrowError(ErrorCodes.NotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (delivery.Status != DeliveryStatus.PendingApproval)
        {
            ThrowError(ErrorCodes.ApprovalNotPending, StatusCodes.Status409Conflict);
            return;
        }

        var now = DateTime.UtcNow;
        delivery.DenyReason = reason.Length > Delivery.MessageMaxLength
            ? reason[..Delivery.MessageMaxLength]
            : reason;
        delivery.Status = DeliveryStatus.Revoked;
        delivery.DeletedAt = now;
        delivery.PendingRecipients = null;
        delivery.UpdatedAt = now;

        db.Notifications.Add(new Notification
        {
            UserId = delivery.SenderId!.Value,
            Type = NotificationType.ApprovalDecided,
            Title = delivery.FileName,
            LinkUrl = RoutePaths.AccountDeliveries,
        });

        await db.SaveChangesAsync(ct);

        await workspaces.AuditAsync(seat.WorkspaceId, seat.UserId!.Value, SecurityEventType.DeliveryDenied,
            HttpContext, ct, JsonSerializer.Serialize(new { delivery.FileName, reason = delivery.DenyReason }));

        await Resolve<ApprovalNotifier>().SendDenialAsync(delivery, ct);

        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// Emails for the approval gate (issue #162). Split out because the release path has to send
/// the very messages the commit path deliberately withheld, and putting that in one place
/// keeps the two halves of the promise together.
/// </summary>
public sealed class ApprovalNotifier(
    EmailSender emailSender,
    Microsoft.Extensions.Options.IOptions<EmailOptions> emailOptions,
    AppDbContext db,
    ILogger<ApprovalNotifier> logger)
{
    private const string ReleaseSubject = "A file has been shared with you";
    private const string DenialSubject = "Your share was not approved";

    public static List<string> SplitRecipients(string? packed) =>
        string.IsNullOrWhiteSpace(packed)
            ? []
            : packed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public async Task SendReleaseAsync(Delivery delivery, List<string> recipients, CancellationToken ct)
    {
        if (!emailSender.IsConfigured || recipients.Count == 0)
            return;

        var link = $"{emailOptions.Value.BaseUrl.TrimEnd('/')}{RoutePaths.DeliveryPage}{delivery.ShareCode}";
        var body = $"""
            <p>{System.Net.WebUtility.HtmlEncode(delivery.FileName)} is ready for you.</p>
            <p><a href="{link}">Open the delivery</a></p>
            """;

        foreach (var recipient in recipients)
        {
            try
            {
                await emailSender.SendAsync(recipient, "QuickShare", ReleaseSubject, body, ct);
            }
            catch (Exception ex)
            {
                // One bad address must not strand the rest of the list.
                logger.LogWarning(ex, "Approval release email failed for delivery {DeliveryId}", delivery.Id);
            }
        }
    }

    public async Task SendDenialAsync(Delivery delivery, CancellationToken ct)
    {
        if (!emailSender.IsConfigured || delivery.SenderId is not { } senderId)
            return;

        var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == senderId, ct);
        if (sender is null || string.IsNullOrWhiteSpace(sender.Email))
            return;

        var body = $"""
            <p>Your share <strong>{System.Net.WebUtility.HtmlEncode(delivery.FileName)}</strong> was not approved.</p>
            <p>Reason: {System.Net.WebUtility.HtmlEncode(delivery.DenyReason ?? string.Empty)}</p>
            <p>The file is in your trash and can be recovered from there.</p>
            """;

        try
        {
            await emailSender.SendAsync(sender.Email, sender.Name, DenialSubject, body, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Approval denial email failed for delivery {DeliveryId}", delivery.Id);
        }
    }
}
