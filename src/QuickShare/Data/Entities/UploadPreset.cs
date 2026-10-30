namespace QuickShare.Data.Entities;

/// <summary>
/// A saved send configuration for Pro/Studio users (issue #100).
///
/// A preset stores the options a sender fills in on every upload — expiry, password, burn,
/// privacy, recipients — so a repeat sender loads them in one click instead of re-configuring
/// five fields each time.
///
/// The password is stored in plaintext: it is the share-link password the *recipient* will
/// enter, not an auth credential, and the sender must be able to read it back to verify the
/// preset before loading. This is the same trade-off as a password manager's visible passwords.
/// </summary>
public record UploadPreset : BaseAuditEntity
{
    public const int NameMaxLength = 80;
    public const int PasswordMaxLength = 120;
    public const int RecipientsMaxLength = 1000;
    public const int MaxPresetsPerUser = 10;

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Link lifetime in hours. Null = use plan default.</summary>
    public double? ExpiryHours { get; set; }

    /// <summary>Plaintext share password for pre-filling the upload form.</summary>
    public string? Password { get; set; }

    public bool BurnAfterDownload { get; set; }

    public bool PrivacyMode { get; set; }

    /// <summary>Comma-separated recipient email addresses.</summary>
    public string? Recipients { get; set; }

    public User Owner { get; init; } = null!;
}
