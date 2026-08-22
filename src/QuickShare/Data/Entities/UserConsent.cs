namespace QuickShare.Data.Entities;

/// <summary>
/// A recorded, versioned agreement (issue #230).
///
/// Vietnam's Decree 13 requires consent that is specific — to the processing, and separately to
/// the transfer of personal data abroad, which is what our storage does. \"Specific\" is only
/// provable if the record names the text that was shown, so the version travels with it.
///
/// Withdrawal marks the row rather than deleting it: *consented on Tuesday, withdrew on Friday*
/// is the record that matters, and erasing the second half would erase the part that protects
/// the person. The row itself holds nothing about them that the account did not already hold.
/// </summary>
public record UserConsent
{
    public const int KindMaxLength = 24;
    public const int VersionMaxLength = 40;

    public Guid Id { get; init; }
    public Guid UserId { get; init; }

    /// <summary>Which consent this is. One row per kind and version.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>The version of the text the person was shown when they agreed.</summary>
    public string Version { get; init; } = string.Empty;

    public DateTime GrantedAt { get; init; } = DateTime.UtcNow;
    /// <summary>Set when withdrawn. Null means it still stands.</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Hashed, like every other address recorded here.</summary>
    public string IpHash { get; init; } = string.Empty;

    public User User { get; init; } = null!;
}
