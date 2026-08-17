using Microsoft.Extensions.Options;
using QuickShare.Infrastructure.FeatureFlags;

namespace QuickShare.Features.Feedback;

/// <summary>
/// Anonymous one-click feedback (issue #35) — sentiment + optional text from the
/// send/download pages. No account, no PII. Rate-limited by the global middleware.
/// </summary>
public sealed class SubmitFeedbackEndpoint : Endpoint<SubmitFeedbackRequest>
{
    private const int MaxMessageLength = 1000;

    public override void Configure()
    {
        Post("/api/feedback");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(SubmitFeedbackRequest req, CancellationToken ct)
    {
        if (!IsValidSentiment(req.Sentiment))
        {
            ThrowError("Invalid sentiment", 400);
            return;
        }
        if (req.Message is { Length: > MaxMessageLength })
        {
            ThrowError("Message too long", 400);
            return;
        }

        var db = Resolve<AppDbContext>();
        db.Feedbacks.Add(new QuickShare.Data.Entities.Feedback
        {
            Id = Guid.CreateVersion7(),
            Sentiment = req.Sentiment,
            Message = req.Message,
            Page = req.Page is "send" or "download" ? req.Page : "unknown"
        });
        await db.SaveChangesAsync(ct);

        var analytics = Resolve<AnalyticsService>();
        await analytics.RecordAsync(AnalyticsEvents.FeedbackSubmitted, ct: ct);

        await Send.NoContentAsync(ct);
    }

    private static bool IsValidSentiment(string sentiment) =>
        sentiment is FeedbackSentiment.Up or FeedbackSentiment.Neutral or FeedbackSentiment.Down;
}

public class SubmitFeedbackRequest
{
    public string Sentiment { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? Page { get; set; }
}

public record ListFeedbackQuery() : IRequest<List<FeedbackListItem>>;

public sealed class ListFeedbackHandler(AppDbContext db) : IRequestHandler<ListFeedbackQuery, List<FeedbackListItem>>
{
    public async ValueTask<List<FeedbackListItem>> Handle(ListFeedbackQuery _, CancellationToken cancellationToken)
    {
        return await db.Feedbacks
            .AsNoTracking()
            .OrderByDescending(f => f.CreatedAt)
            .Take(100)
            .Select(f => new FeedbackListItem(f.Id, f.Sentiment, f.Message, f.Page, f.CreatedAt))
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Admin-only list of recent feedback (issue #35).</summary>
public sealed class ListFeedbackEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/admin/feedback");
        Description(b => b.WithTags(ApiTags.Admin));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!User.IsAdmin(Resolve<IOptions<AdminOptions>>()))
        {
            ThrowError("Forbidden", 403);
            return;
        }

        var feedback = await Resolve<IMediator>().Send<List<FeedbackListItem>>(new ListFeedbackQuery(), ct);
        await Send.OkAsync(feedback, ct);
    }
}

public record FeedbackListItem(Guid Id, string Sentiment, string? Message, string Page, DateTime CreatedAt);
