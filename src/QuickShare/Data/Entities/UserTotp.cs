namespace QuickShare.Data.Entities;

/// <summary>
/// A TOTP authenticator enrolment (issue #128, RFC 6238).
///
/// Sign-in here is OAuth-only, so this is a genuine second factor rather than a password's
/// sidekick: the first factor is the identity provider, the second is a device the account
/// holder physically has. That also means enrolment is per-account and survives switching
/// between Google and GitHub sign-in on the same address.
///
/// The secret is stored as it must be — recoverable, because verification recomputes the code
/// from it. It is never sent to the client after confirmation, and never appears in an API
/// response or an audit payload.
/// </summary>
public record UserTotp : BaseAuditEntity
{
    /// <summary>160 bits, the RFC 4226 recommendation, as 32 base32 characters.</summary>
    public const int SecretLength = 32;
    public const int DigitCount = 6;
    public const int PeriodSeconds = 30;
    /// <summary>One step either side, so a slow entry or a mildly drifted clock still verifies.</summary>
    public const int AllowedDriftSteps = 1;
    public const int RecoveryCodeCount = 10;
    public const int RecoveryCodesMaxLength = 1024;

    public Guid UserId { get; set; }
    /// <summary>Base32, no padding — the format every authenticator app reads from an otpauth URI.</summary>
    public string Secret { get; set; } = string.Empty;
    /// <summary>Null until the first correct code proves the app actually holds the secret.</summary>
    public DateTime? ConfirmedAt { get; set; }
    /// <summary>
    /// The last time step a code was accepted for. A code is single-use: without this, one
    /// shoulder-surfed six digits stays valid for the rest of its 30-second window.
    /// </summary>
    public long LastUsedStep { get; set; }
    /// <summary>SHA-256 of each unused recovery code, JSON array. The raw codes are shown once.</summary>
    public string RecoveryCodeHashes { get; set; } = "[]";

    public User User { get; init; } = null!;

    public bool IsConfirmed => ConfirmedAt is not null;
}
