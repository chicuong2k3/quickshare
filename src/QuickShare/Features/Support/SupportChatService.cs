using System.Security.Cryptography;
using QuickShare.Infrastructure.Email;

namespace QuickShare.Features.Support;

public sealed record SupportMessageDto(
    Guid Id,
    string SenderRole,
    string SenderName,
    string Body,
    DateTime CreatedAt);

public sealed record SupportChatThreadDto(
    Guid Id,
    string TicketNumber,
    string Subject,
    string Plan,
    string QueueTier,
    string Status,
    string? EscalatedEmail,
    DateTime? EscalatedAt,
    DateTime LastMessageAt,
    IReadOnlyList<SupportMessageDto> Messages);

public sealed class SendSupportMessageRequest
{
    public string? Subject { get; set; }
    public string? Body { get; set; }
}

public sealed class EscalateSupportChatRequest
{
    public string? Email { get; set; }
}

public static class SupportFaqBot
{
    public static string AnswerFor(string userMessage, string ticketNumber, bool isPaidPriority)
    {
        var lower = userMessage.ToLowerInvariant();
        if (lower.Contains("billing", StringComparison.Ordinal)
            || lower.Contains("invoice", StringComparison.Ordinal)
            || lower.Contains("payment", StringComparison.Ordinal)
            || lower.Contains("refund", StringComparison.Ordinal))
        {
            return isPaidPriority
                ? $"[Priority Queue {ticketNumber}] A billing specialist has been notified (SLA < 15m). You can also view VAT invoices at /account/invoices or manage subscriptions at /account/billing."
                : $"[FAQ Bot · {ticketNumber}] You can view receipts and manage your subscription at /account/billing, or download VAT e-invoices at /account/invoices. Need human help? Click 'Escalate to Email'.";
        }

        if (lower.Contains("upload", StringComparison.Ordinal)
            || lower.Contains("stuck", StringComparison.Ordinal)
            || lower.Contains("resume", StringComparison.Ordinal))
        {
            return isPaidPriority
                ? $"[Priority Queue {ticketNumber}] An engineer is reviewing your transfer session. Note: multipart uploads auto-resume from the last completed 25 MB chunk."
                : $"[FAQ Bot · {ticketNumber}] Large uploads split into 25 MB chunks and can be resumed if your connection drops. Pro/Studio users also bypass the large-file queue.";
        }

        if (lower.Contains("password", StringComparison.Ordinal)
            || lower.Contains("otp", StringComparison.Ordinal)
            || lower.Contains("encrypt", StringComparison.Ordinal))
        {
            return $"[Support · {ticketNumber}] Zero-knowledge encrypted links keep the AES-256-GCM key in the URL fragment (#key=...) — our servers never see it. See /help for security details.";
        }

        return isPaidPriority
            ? $"[Priority Live Queue · {ticketNumber}] Your message is routed to our priority Pro/Studio support desk. An agent will reply here shortly, or we will email you if you step away."
            : $"[FAQ Assistant · {ticketNumber}] Thanks for reaching out! Browse instant guides at /help or click 'Escalate to Email' to open a support ticket with our team.";
    }
}

public sealed class GetSupportChatEndpoint : EndpointWithoutRequest<SupportChatThreadDto?>
{
    public override void Configure()
    {
        Get(RoutePaths.SupportChatApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Get the active in-app support chat thread and message history");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);

        var thread = await db.SupportChatThreads
            .AsNoTracking()
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        if (thread is null)
        {
            await Send.OkAsync(null, ct);
            return;
        }

        await Send.OkAsync(await LoadDtoAsync(db, thread, ct), ct);
    }

    internal static async Task<SupportChatThreadDto> LoadDtoAsync(AppDbContext db, SupportChatThread thread, CancellationToken ct)
    {
        var messages = await db.SupportChatMessages
            .AsNoTracking()
            .Where(m => m.ThreadId == thread.Id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new SupportMessageDto(m.Id, m.SenderRole, m.SenderName, m.Body, m.CreatedAt))
            .ToListAsync(ct);

        return new SupportChatThreadDto(
            thread.Id,
            thread.TicketNumber,
            thread.Subject,
            thread.Plan,
            thread.QueueTier,
            thread.Status,
            thread.EscalatedEmail,
            thread.EscalatedAt,
            thread.LastMessageAt,
            messages);
    }
}

public sealed class SendSupportMessageEndpoint : Endpoint<SendSupportMessageRequest, SupportChatThreadDto>
{
    public override void Configure()
    {
        Post(RoutePaths.SupportChatApi);
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Send a message in the in-app support chat (priority live queue for Pro/Studio, FAQ bot for Free)");
    }

    public override async Task HandleAsync(SendSupportMessageRequest req, CancellationToken ct)
    {
        var body = req.Body?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(body) || body.Length > SupportChatThread.MessageMaxLength)
        {
            ThrowError(ErrorCodes.SupportMessageEmpty, StatusCodes.Status400BadRequest);
            return;
        }

        var db = Resolve<AppDbContext>();
        var user = await User.GetUserAsync(db, ct);
        var isPaid = user.Plan is PlanTierLimits.Pro or PlanTierLimits.Studio;
        var queueTier = isPaid ? SupportChatThread.QueuePriorityLive : SupportChatThread.QueueFaqBot;

        var thread = await db.SupportChatThreads
            .Where(t => t.UserId == user.Id && t.Status != SupportChatThread.StatusResolved)
            .OrderByDescending(t => t.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        if (thread is null)
        {
            var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(3));
            var subject = string.IsNullOrWhiteSpace(req.Subject)
                ? (body.Length > 60 ? body[..60] : body)
                : req.Subject.Trim()[..Math.Min(req.Subject.Trim().Length, SupportChatThread.SubjectMaxLength)];

            thread = new SupportChatThread
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                TicketNumber = $"SUP-{now:yyyyMMdd}-{suffix}",
                Subject = subject,
                Plan = user.Plan,
                QueueTier = queueTier,
                Status = SupportChatThread.StatusOpen,
                LastMessageAt = now,
            };
            db.SupportChatThreads.Add(thread);

            db.SecurityEvents.Add(new SecurityEvent
            {
                Id = Guid.CreateVersion7(),
                UserId = user.Id,
                EventType = SecurityEventType.SupportChatStarted,
                MetadataJson = $"{{\"ticket\":\"{thread.TicketNumber}\",\"tier\":\"{queueTier}\"}}",
            });
        }
        else
        {
            thread.Plan = user.Plan;
            thread.QueueTier = queueTier;
            thread.LastMessageAt = now;
        }

        db.SupportChatMessages.Add(new SupportChatMessage
        {
            Id = Guid.CreateVersion7(),
            ThreadId = thread.Id,
            SenderUserId = user.Id,
            SenderRole = SupportChatMessage.SenderUser,
            SenderName = string.IsNullOrWhiteSpace(user.Name) ? user.Email : user.Name,
            Body = body,
        });

        var autoReply = SupportFaqBot.AnswerFor(body, thread.TicketNumber, isPaid);
        db.SupportChatMessages.Add(new SupportChatMessage
        {
            Id = Guid.CreateVersion7(),
            ThreadId = thread.Id,
            SenderUserId = null,
            SenderRole = isPaid ? SupportChatMessage.SenderAgent : SupportChatMessage.SenderBot,
            SenderName = isPaid ? "QuickShare Priority Support" : "QuickShare Help Bot",
            Body = autoReply,
        });

        await db.SaveChangesAsync(ct);
        await Send.OkAsync(await GetSupportChatEndpoint.LoadDtoAsync(db, thread, ct), ct);
    }
}

public sealed class EscalateSupportChatEndpoint : Endpoint<EscalateSupportChatRequest, SupportChatThreadDto>
{
    public override void Configure()
    {
        Post($"{RoutePaths.SupportChatApi}/escalate");
        Description(b => b.WithTags(ApiTags.Users));
        Summary(s => s.Summary = "Escalate the active support chat thread to an email ticket");
    }

    public override async Task HandleAsync(EscalateSupportChatRequest req, CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var emailSender = Resolve<EmailSender>();
        var user = await User.GetUserAsync(db, ct);

        var thread = await db.SupportChatThreads
            .Where(t => t.UserId == user.Id)
            .OrderByDescending(t => t.LastMessageAt)
            .FirstOrDefaultAsync(ct);

        if (thread is null)
        {
            ThrowError(ErrorCodes.SupportChatNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var targetEmail = string.IsNullOrWhiteSpace(req.Email) ? user.Email : req.Email.Trim();
        thread.Status = SupportChatThread.StatusEscalated;
        thread.EscalatedEmail = targetEmail;
        thread.EscalatedAt = DateTime.UtcNow;

        db.SupportChatMessages.Add(new SupportChatMessage
        {
            Id = Guid.CreateVersion7(),
            ThreadId = thread.Id,
            SenderRole = SupportChatMessage.SenderAgent,
            SenderName = "QuickShare Support Desk",
            Body = $"Ticket {thread.TicketNumber} has been escalated to email ({targetEmail}). Replies to that thread are tracked under your ticket ID.",
        });

        db.SecurityEvents.Add(new SecurityEvent
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            EventType = SecurityEventType.SupportChatEscalated,
            MetadataJson = $"{{\"ticket\":\"{thread.TicketNumber}\",\"email\":\"{targetEmail}\"}}",
        });

        await db.SaveChangesAsync(ct);

        if (emailSender.IsConfigured && !string.IsNullOrWhiteSpace(targetEmail))
        {
            try
            {
                await emailSender.SendAsync(
                    targetEmail,
                    user.Name,
                    $"[{thread.TicketNumber}] Support Request: {thread.Subject}",
                    $"<p>Your support chat (<strong>{thread.TicketNumber}</strong>) has been escalated to our email support desk.</p>",
                    ct);
            }
            catch
            {
                // Best-effort email notification
            }
        }

        await Send.OkAsync(await GetSupportChatEndpoint.LoadDtoAsync(db, thread, ct), ct);
    }
}
