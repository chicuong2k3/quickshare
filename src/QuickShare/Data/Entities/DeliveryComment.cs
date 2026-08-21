namespace QuickShare.Data.Entities;

/// <summary>
/// Who may talk on a workspace's deliveries (issue #289). A workspace-level ceiling: it can
/// close conversation the sender opened, never open one they did not.
/// </summary>
public static class CommentPolicy
{
    /// <summary>No comments anywhere in the workspace, whatever a sender ticks.</summary>
    public const string Disabled = "disabled";
    /// <summary>Recipients and the sender's own team. The default — a client asking a question is the point.</summary>
    public const string Recipients = "recipients";
    /// <summary>Signed-in workspace members only; recipients can read but not post.</summary>
    public const string MembersOnly = "members";

    public static readonly string[] All = [Disabled, Recipients, MembersOnly];
}

/// <summary>
/// One message on a delivery (issue #289).
///
/// A share is one-way by default; a client who wants to ask "is section 3 final?" currently
/// leaves the product to do it, and the answer lives in somebody's inbox. Comments keep that
/// thread attached to the file it is about.
///
/// Recipients have no account here, so an anonymous author supplies a display name and is
/// identified for abuse purposes by a hashed IP — the same treatment every other address in this
/// system gets (privacy #26). That means a name is a claim, not an identity, and the UI says so
/// rather than implying a verification we never did.
/// </summary>
public record DeliveryComment : BaseAuditEntity
{
    public const int BodyMaxLength = 2000;
    public const int AuthorNameMaxLength = 60;
    /// <summary>One level of replies. Deeper threads need collapsing UI nobody has asked for yet.</summary>
    public const int MaxDepth = 2;
    /// <summary>Anonymous posts per delivery per address per hour. Enough for a conversation, not a flood.</summary>
    public const int RateLimitPerHour = 20;

    public Guid DeliveryId { get; set; }
    /// <summary>Null for a top-level message; a reply may never itself be replied to.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Set when a signed-in user posted. Null means an anonymous recipient.</summary>
    public Guid? AuthorUserId { get; set; }
    /// <summary>Display name as given. For a signed-in author it is copied from the account.</summary>
    public string AuthorName { get; set; } = string.Empty;
    /// <summary>Hashed, never raw — the only handle we keep on an anonymous author.</summary>
    public string AuthorIpHash { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;
    public DateTime? EditedAt { get; set; }
    /// <summary>
    /// Soft delete. The row stays so a reply does not lose the message it answers; the body is
    /// blanked on removal, because keeping deleted text around is how a "delete" turns out not
    /// to have been one.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    public Delivery Delivery { get; init; } = null!;

    public bool IsDeleted => DeletedAt is not null;
}

/// <summary>Chat platform a workspace posts its deliveries into (issue #180).</summary>
public static class ChannelKind
{
    public const string Slack = "slack";
    public const string Teams = "teams";

    public static readonly string[] All = [Slack, Teams];
}

/// <summary>
/// A workspace's chat-channel webhook (issue #180).
///
/// Enterprise recipients live in Slack and Teams, and a file-sharing tool that never reaches the
/// channel loses the collaboration slot to one that does. Both platforms accept a plain
/// <c>{"text": …}</c> body on an incoming-webhook URL, so one shape serves both — a per-platform
/// card format would be two renderers to keep in step for no gain a channel reader would notice.
///
/// The URL *is* the credential: anyone holding it can post to that channel. It is stored, never
/// returned, and only ever posted to over HTTPS.
/// </summary>
public record WorkspaceChannelHook : BaseAuditEntity
{
    public const int UrlMaxLength = 500;
    public const int ErrorMaxLength = 400;
    /// <summary>Consecutive failures before the hook is parked. A revoked webhook 404s forever otherwise.</summary>
    public const int MaxConsecutiveFailures = 10;

    public Guid WorkspaceId { get; set; }
    public string Kind { get; set; } = ChannelKind.Slack;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>Announce a new share in the channel.</summary>
    public bool NotifyOnDelivery { get; set; } = true;
    /// <summary>Announce comments, so a question in the product does not sit unseen.</summary>
    public bool NotifyOnComment { get; set; } = true;

    public DateTime? LastPostedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public string? LastError { get; set; }

    public Workspace Workspace { get; init; } = null!;

    public bool IsParked => ConsecutiveFailures >= MaxConsecutiveFailures;
}
