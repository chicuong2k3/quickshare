namespace QuickShare.Data.Entities;

/// <summary>
/// Seat roles inside a workspace (issue #106). Ordered least-to-most privileged so
/// gates can compare with &lt;=: Owner outranks Admin outranks Member outranks Viewer.
/// Exactly one Owner row exists per workspace at any time — the invariant #252's
/// transfer flow moves rather than breaks.
/// </summary>
public enum WorkspaceRole
{
    Owner,
    Admin,
    Member,
    Viewer,
}

/// <summary>
/// A team workspace (issue #106): one bill, one pooled monthly quota, one shared
/// delivery history. The owner's subscription is the workspace's plan — members
/// inherit it while their seat is active and fall back to their own plan when the
/// seat is revoked (see <c>WorkspaceService.SyncMemberPlansAsync</c>).
/// </summary>
public record Workspace : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int MinSeats = 2;
    public const int MaxSeats = 25;

    public string Name { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    /// <summary>Seats bought, owner included. Invites are refused once seats are full.</summary>
    public int Seats { get; set; } = MinSeats;

    /// <summary>
    /// Every share from this workspace waits for an admin before its link works (issue #162).
    /// Folder defaults can tighten this on individual folders; nothing can loosen it.
    /// </summary>
    public bool RequireDeliveryApproval { get; set; }

    /// <summary>
    /// Lets an admin clear their own share. Off by default — the control exists because
    /// somebody wanted a second pair of eyes, and self-approval is not a second pair of eyes.
    /// </summary>
    public bool AllowSelfApproval { get; set; }

    /// <summary>
    /// Ceiling on delivery comments across the workspace (issue #289). It can close a
    /// conversation a sender opened; it can never open one they did not.
    /// </summary>
    public string CommentPolicy { get; set; } = Entities.CommentPolicy.Recipients;

    public User Owner { get; init; } = null!;
}

/// <summary>
/// One seat in a workspace (issue #106). A row is created the moment an invite is
/// sent — the seat is consumed by the pending invite, so two admins cannot oversell
/// the last slot. <see cref="UserId"/> stays null until the invite is accepted.
/// </summary>
public record WorkspaceMember : BaseAuditEntity
{
    public const int EmailMaxLength = 254;
    public const int TokenHashLength = 64;
    public const int InviteValidDays = 7;

    public Guid WorkspaceId { get; set; }
    /// <summary>Null while the invite is outstanding; set on accept.</summary>
    public Guid? UserId { get; set; }
    public string InviteEmail { get; set; } = string.Empty;
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    /// <summary>SHA-256 of the emailed invite token. The raw token is never stored.</summary>
    public string? InviteTokenHash { get; set; }
    public DateTime? InviteExpiresAt { get; set; }
    public DateTime? JoinedAt { get; set; }
    /// <summary>Set when a seat is revoked. The row is kept so the audit log stays whole.</summary>
    public DateTime? RevokedAt { get; set; }

    public Workspace Workspace { get; init; } = null!;

    /// <summary>Claimed seat: accepted and not revoked. Pending invites hold a seat too.</summary>
    public bool IsActive => JoinedAt is not null && RevokedAt is null;
    public bool IsPending => JoinedAt is null && RevokedAt is null;
    /// <summary>Admin or Owner — the roles that may manage members, audit and reports.</summary>
    public bool CanAdminister => IsActive && Role is WorkspaceRole.Owner or WorkspaceRole.Admin;
}

/// <summary>
/// A pending ownership handover (issue #252). Ownership never moves on the sender's
/// word alone: the successor accepts inside a 7-day window, because the transfer
/// makes them the bill payer and consent to that cannot be assumed.
/// </summary>
public record WorkspaceTransfer : BaseAuditEntity
{
    public const int ValidDays = 7;

    public Guid WorkspaceId { get; set; }
    public Guid FromUserId { get; set; }
    public Guid ToUserId { get; set; }
    /// <summary>SHA-256 of the emailed accept token. The raw token is never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public Workspace Workspace { get; init; } = null!;

    public bool IsOpen(DateTime now) => AcceptedAt is null && CancelledAt is null && ExpiresAt > now;
}
