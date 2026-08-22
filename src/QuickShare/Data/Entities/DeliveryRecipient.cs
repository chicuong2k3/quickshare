namespace QuickShare.Data.Entities;

/// <summary>
/// Somebody the sender addressed this delivery to, and what they did with it (issue #37).
///
/// The multi-recipient half of #37 shipped long ago: N addresses, per-plan caps, one mail each.
/// The half that was missing is the one the issue's acceptance actually names — *who opened it,
/// who downloaded it*. A sender who mails five people and sees "3 downloads" still does not know
/// which two to chase.
///
/// Attribution works by giving each recipient their own token in their own mail. That is a
/// tracking identifier and it behaves like one: a forwarded link attributes the forwarder's
/// recipient, because the token travels with the URL. It marks nothing and gates nothing — it
/// cannot open a share that is closed, and removing it costs a recipient nothing but their row.
/// </summary>
public record DeliveryRecipient
{
    public const int EmailMaxLength = 254;
    /// <summary>Hex characters. Long enough not to be guessed, short enough to sit in a URL.</summary>
    public const int TokenLength = 24;
    public const string TokenQueryKey = "t";

    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }

    /// <summary>The address as the sender typed it. They chose it; showing it back is not a leak.</summary>
    public string Email { get; init; } = string.Empty;

    public string Token { get; init; } = string.Empty;

    public DateTime SentAt { get; init; } = DateTime.UtcNow;
    /// <summary>First time this recipient's link opened the page. Null until then.</summary>
    public DateTime? OpenedAt { get; set; }
    /// <summary>First time they actually took the file. Opening is interest; this is delivery.</summary>
    public DateTime? DownloadedAt { get; set; }

    public Delivery Delivery { get; init; } = null!;
}
