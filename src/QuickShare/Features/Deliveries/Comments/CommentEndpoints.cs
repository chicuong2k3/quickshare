using Microsoft.Extensions.Options;
using QuickShare.Extensions;
using QuickShare.Features.Users.Shared;
using QuickShare.Features.Workspaces.Channels;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Deliveries.Comments;

public sealed record CommentDto(
    Guid Id,
    Guid? ParentId,
    string AuthorName,
    bool IsTeamMember,
    string Body,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime? EditedAt);

public sealed record CommentThreadDto(bool CanPost, string Policy, IReadOnlyList<CommentDto> Comments);

public sealed class PostCommentRequest
{
    public string? Body { get; set; }
    public Guid? ParentId { get; set; }
    /// <summary>Display name, required for anonymous recipients; ignored when signed in.</summary>
    public string? AuthorName { get; set; }
}

/// <summary>
/// Resolves whether a delivery accepts comments, and from whom (issue #289).
///
/// Three gates, each able only to close: the sender's per-delivery switch, the workspace policy,
/// and privacy mode. Privacy mode (#187) disables comments outright — a share that deliberately
/// keeps no download record should not accumulate a public thread instead.
/// </summary>
public sealed class CommentGate(AppDbContext db)
{
    public sealed record Verdict(bool CanRead, bool CanPost, string Policy);

    public async Task<Verdict> EvaluateAsync(Delivery delivery, bool isSignedInMember, CancellationToken ct)
    {
        if (!delivery.CommentsEnabled || delivery.PrivacyMode)
            return new Verdict(false, false, CommentPolicy.Disabled);

        var policy = delivery.WorkspaceId is { } workspaceId
            ? await db.Workspaces
                .AsNoTracking()
                .Where(w => w.Id == workspaceId)
                .Select(w => w.CommentPolicy)
                .FirstOrDefaultAsync(ct) ?? CommentPolicy.Recipients
            : CommentPolicy.Recipients;

        return policy switch
        {
            CommentPolicy.Disabled => new Verdict(false, false, policy),
            // Recipients may still read the thread; only posting is held to the team.
            CommentPolicy.MembersOnly => new Verdict(true, isSignedInMember, policy),
            _ => new Verdict(true, true, policy),
        };
    }
}

/// <summary>
/// GET /api/deliveries/{ShareCode}/comments — the thread on a delivery (issue #289).
///
/// Anonymous, because the audience is the recipient reading the file. Keyed by share code rather
/// than delivery id: the code is what the recipient holds, and it is already the capability that
/// grants access to the file itself.
/// </summary>
public sealed class ListCommentsEndpoint : EndpointWithoutRequest<CommentThreadDto>
{
    public override void Configure()
    {
        Get("/api/deliveries/{ShareCode}/comments");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "List comments on a delivery";
            s.Description = "Public, keyed by share code. Returns an empty, non-postable thread when the sender has not opened comments.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var shareCode = Route<string>("ShareCode") ?? string.Empty;

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode && d.DeletedAt == null, ct);

        if (delivery is null)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var member = await CommentAuthor.IsWorkspaceMemberAsync(db, HttpContext, delivery.WorkspaceId, ct);
        var verdict = await Resolve<CommentGate>().EvaluateAsync(delivery, member, ct);

        if (!verdict.CanRead)
        {
            await Send.OkAsync(new CommentThreadDto(false, verdict.Policy, []), ct);
            return;
        }

        var comments = await db.DeliveryComments
            .AsNoTracking()
            .Where(c => c.DeliveryId == delivery.Id)
            .OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto(
                c.Id,
                c.ParentId,
                c.AuthorName,
                c.AuthorUserId != null,
                // A removed message keeps its place so replies still make sense, but its text
                // is gone — a "delete" that leaves the words readable was not one.
                c.DeletedAt == null ? c.Body : string.Empty,
                c.DeletedAt != null,
                c.CreatedAt,
                c.EditedAt))
            .ToListAsync(ct);

        await Send.OkAsync(new CommentThreadDto(verdict.CanPost, verdict.Policy, comments), ct);
    }
}

/// <summary>
/// POST /api/deliveries/{ShareCode}/comments — say something on a delivery (issue #289).
///
/// Anonymous recipients supply a display name and are rate-limited by hashed address. A name is
/// a claim, not an identity, and the thread marks which authors are signed-in members so a
/// reader can tell the difference rather than being invited to assume.
/// </summary>
public sealed class PostCommentEndpoint : Endpoint<PostCommentRequest, CommentDto>
{
    public override void Configure()
    {
        Post("/api/deliveries/{ShareCode}/comments");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Post a comment on a delivery";
            s.Description = "Public when the sender opened comments and the workspace policy allows it. Replies are one level deep; anonymous authors are rate-limited per address.";
        });
    }

    public override async Task HandleAsync(PostCommentRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var shareCode = Route<string>("ShareCode") ?? string.Empty;

        var delivery = await db.Deliveries
            .FirstOrDefaultAsync(d => d.ShareCode == shareCode && d.DeletedAt == null, ct);

        if (delivery is null)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var author = await CommentAuthor.ResolveAsync(db, HttpContext, delivery, req.AuthorName, ct);
        var verdict = await Resolve<CommentGate>().EvaluateAsync(delivery, author.IsMember, ct);

        if (!verdict.CanPost)
        {
            ThrowError(ErrorCodes.CommentsDisabled, StatusCodes.Status403Forbidden);
            return;
        }

        var body = req.Body?.Trim();
        if (string.IsNullOrEmpty(body))
        {
            ThrowError(ErrorCodes.CommentBodyRequired, StatusCodes.Status400BadRequest);
            return;
        }

        if (body.Length > DeliveryComment.BodyMaxLength)
            body = body[..DeliveryComment.BodyMaxLength];

        if (string.IsNullOrEmpty(author.Name))
        {
            ThrowError(ErrorCodes.CommentAuthorRequired, StatusCodes.Status400BadRequest);
            return;
        }

        if (req.ParentId is { } parentId)
        {
            var parent = await db.DeliveryComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parentId && c.DeliveryId == delivery.Id, ct);

            if (parent is null)
            {
                ThrowError(ErrorCodes.CommentNotFound, StatusCodes.Status404NotFound);
                return;
            }

            // One level. A reply to a reply needs collapsing UI nobody has asked for.
            if (parent.ParentId is not null)
            {
                ThrowError(ErrorCodes.CommentTooDeep, StatusCodes.Status400BadRequest);
                return;
            }
        }

        // Anonymous posting is the abuse surface; signed-in authors are already accountable.
        if (author.UserId is null)
        {
            var since = DateTime.UtcNow.AddHours(-1);
            var recent = await db.DeliveryComments
                .CountAsync(c => c.DeliveryId == delivery.Id
                    && c.AuthorIpHash == author.IpHash
                    && c.CreatedAt >= since, ct);

            if (recent >= DeliveryComment.RateLimitPerHour)
            {
                ThrowError(ErrorCodes.CommentRateLimited, StatusCodes.Status429TooManyRequests);
                return;
            }
        }

        var comment = new DeliveryComment
        {
            Id = Guid.CreateVersion7(),
            DeliveryId = delivery.Id,
            ParentId = req.ParentId,
            AuthorUserId = author.UserId,
            AuthorName = author.Name,
            AuthorIpHash = author.IpHash,
            Body = body,
            CreatedAt = DateTime.UtcNow,
        };

        db.DeliveryComments.Add(comment);

        // The sender should not have to poll their own share to find out somebody asked a
        // question. Skipped when the sender is the one talking.
        if (delivery.SenderId is { } senderId && senderId != author.UserId)
        {
            db.Notifications.Add(new Notification
            {
                UserId = senderId,
                Type = NotificationType.DeliveryCommented,
                Title = delivery.FileName,
                LinkUrl = $"{RoutePaths.DeliveryPage}{delivery.ShareCode}",
            });
        }

        await db.SaveChangesAsync(ct);

        var link = $"{Resolve<IOptions<EmailOptions>>().Value.BaseUrl.TrimEnd('/')}{RoutePaths.DeliveryPage}{delivery.ShareCode}";
        await Resolve<ChannelNotifier>().NotifyCommentAsync(
            delivery.WorkspaceId, delivery.FileName, author.Name, body, link, ct);

        await Send.OkAsync(new CommentDto(
            comment.Id, comment.ParentId, comment.AuthorName, comment.AuthorUserId != null,
            comment.Body, false, comment.CreatedAt, null), ct);
    }
}

/// <summary>
/// DELETE /api/deliveries/{ShareCode}/comments/{Id} — remove a message (issue #289).
///
/// The sender of the delivery may remove anything on it; an anonymous author cannot remove their
/// own, because "same hashed address" is not proof of authorship and honouring it would let
/// anyone behind the same NAT delete somebody else's words.
/// </summary>
public sealed class DeleteCommentEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Delete("/api/deliveries/{ShareCode}/comments/{Id}");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s =>
        {
            s.Summary = "Delete a comment";
            s.Description = "The delivery's sender, or the comment's signed-in author. The row is kept so replies keep their context; the text is removed.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var shareCode = Route<string>("ShareCode") ?? string.Empty;

        var comment = await db.DeliveryComments
            .Include(c => c.Delivery)
            .FirstOrDefaultAsync(c => c.Id == Route<Guid>("Id") && c.Delivery.ShareCode == shareCode, ct);

        if (comment is null || comment.IsDeleted)
        {
            ThrowError(ErrorCodes.CommentNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var isSender = comment.Delivery.SenderId == user.Id;
        var isAuthor = comment.AuthorUserId == user.Id;

        if (!isSender && !isAuthor)
        {
            ThrowError(ErrorCodes.WorkspaceForbidden, StatusCodes.Status403Forbidden);
            return;
        }

        comment.DeletedAt = DateTime.UtcNow;
        // Blanked, not merely flagged: keeping the text around is how a delete turns out not to
        // have been one.
        comment.Body = string.Empty;
        comment.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await Send.NoContentAsync(ct);
    }
}

/// <summary>
/// Works out who is talking (issue #289): a signed-in account, or an anonymous recipient with a
/// display name and a hashed address.
/// </summary>
internal static class CommentAuthor
{
    internal sealed record Resolved(Guid? UserId, string Name, string IpHash, bool IsMember);

    public static async Task<Resolved> ResolveAsync(
        AppDbContext db, HttpContext http, Delivery delivery, string? suppliedName, CancellationToken ct)
    {
        var ipHash = DeviceTrackerService.HashIp(http.Connection.RemoteIpAddress?.ToString() ?? string.Empty);
        var user = await http.User.GetUserOrDefaultAsync(db, ct);

        if (user is not null)
        {
            var isMember = await IsMemberAsync(db, user.Id, delivery.WorkspaceId, ct);
            return new Resolved(user.Id, user.Name, ipHash, isMember);
        }

        var name = suppliedName?.Trim() ?? string.Empty;
        if (name.Length > DeliveryComment.AuthorNameMaxLength)
            name = name[..DeliveryComment.AuthorNameMaxLength];

        return new Resolved(null, name, ipHash, false);
    }

    public static async Task<bool> IsWorkspaceMemberAsync(
        AppDbContext db, HttpContext http, Guid? workspaceId, CancellationToken ct)
    {
        var user = await http.User.GetUserOrDefaultAsync(db, ct);
        return user is not null && await IsMemberAsync(db, user.Id, workspaceId, ct);
    }

    private static async Task<bool> IsMemberAsync(AppDbContext db, Guid userId, Guid? workspaceId, CancellationToken ct)
    {
        if (workspaceId is not { } id)
            return false;

        return await db.WorkspaceMembers
            .AnyAsync(m => m.WorkspaceId == id && m.UserId == userId && m.RevokedAt == null && m.JoinedAt != null, ct);
    }
}
