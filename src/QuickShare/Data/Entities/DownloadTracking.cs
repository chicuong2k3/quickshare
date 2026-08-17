namespace QuickShare.Data.Entities;

[DynamicSort]
public record DownloadTracking
{
    public Guid Id { get; init; }
    public Guid DeliveryId { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public DateTime DownloadedAt { get; init; }
    public bool Notified { get; set; }
    /// <summary>Recipient's account id when a signed-in user opts into download history (#220); null for anonymous downloads.</summary>
    public Guid? UserId { get; set; }
    /// <summary>Recipient email captured by the email gate (issue #324); null when the gate is off.</summary>
    public string? RecipientEmail { get; set; }
    /// <summary>Whether the recipient opted into marketing when surrendering their email (#324).</summary>
    public bool MarketingConsent { get; set; }
}
