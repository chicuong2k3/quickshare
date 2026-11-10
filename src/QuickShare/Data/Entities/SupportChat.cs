namespace QuickShare.Data.Entities;

/// <summary>
/// In-app live support chat thread (issue #167).
/// Pro/Studio users enter the priority live agent queue; Free users get instant FAQ bot answers
/// and can escalate to an email ticket with a stable <see cref="TicketNumber"/>.
/// </summary>
[DynamicSort]
public record SupportChatThread : BaseAuditEntity
{
    public const int SubjectMaxLength = 160;
    public const int TicketNumberMaxLength = 24;
    public const int EmailMaxLength = 254;
    public const int MessageMaxLength = 2000;

    public const string QueuePriorityLive = "priority_live";
    public const string QueueFaqBot = "faq_bot";

    public const string StatusOpen = "open";
    public const string StatusEscalated = "escalated";
    public const string StatusResolved = "resolved";

    public Guid UserId { get; init; }
    public string TicketNumber { get; init; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Plan { get; set; } = PlanTierLimits.Free;
    public string QueueTier { get; set; } = QueueFaqBot;
    public string Status { get; set; } = StatusOpen;
    public string? EscalatedEmail { get; set; }
    public DateTime? EscalatedAt { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One message inside a <see cref="SupportChatThread"/> (issue #167).
/// </summary>
[DynamicSort]
public record SupportChatMessage : BaseAuditEntity
{
    public const string SenderUser = "user";
    public const string SenderAgent = "agent";
    public const string SenderBot = "bot";

    public Guid ThreadId { get; init; }
    public Guid? SenderUserId { get; init; }
    public string SenderRole { get; init; } = SenderUser;
    public string SenderName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
}
