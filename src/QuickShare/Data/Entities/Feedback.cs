namespace QuickShare.Data.Entities;

/// <summary>
/// Anonymous in-product feedback (issue #35) — one-click sentiment + optional
/// text from the send and download pages. No account, no PII.
/// </summary>
public record Feedback
{
    public Guid Id { get; init; }
    public string Sentiment { get; init; } = string.Empty;
    public string? Message { get; init; }
    public string Page { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

public static class FeedbackSentiment
{
    public const string Up = "up";
    public const string Neutral = "neutral";
    public const string Down = "down";
}
