namespace QuickShare.Data.Entities;

/// <summary>
/// Recipient contact for a user (issue #316). Captured automatically from the
/// multi-recipient share flow (Name stays null) and enriched by the user via
/// the address book (Name is set). Email is normalized lowercase so a
/// (UserId, Email) unique index gives one row per contact.
/// </summary>
public record RecipientContact
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string? Name { get; set; }
    public DateTime LastUsedAt { get; set; }
    public int UseCount { get; set; }
}