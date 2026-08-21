namespace QuickShare.Data.Entities;

/// <summary>
/// One consumed SAML assertion id (issue #139).
///
/// Deliberately a table and not a cache. The signature check proves an assertion is authentic and
/// the time window proves it is fresh; neither notices the *same* authentic, fresh assertion
/// arriving twice, which is precisely what somebody who intercepted one would send. That makes
/// this a security control, and a security control must not be able to be switched off by a cache
/// setting or lost on a restart — and a per-process cache would not see a replay aimed at a
/// second instance at all.
///
/// The unique index does the work: consuming an assertion is an insert, and a replay is the
/// insert failing. That is atomic across instances in a way a read-then-write never is.
/// </summary>
public record SamlAssertionSeen
{
    public const int AssertionIdMaxLength = 256;

    public Guid Id { get; init; }
    public string AssertionId { get; init; } = string.Empty;
    /// <summary>Purge point. An assertion past its own NotOnOrAfter is refused anyway.</summary>
    public DateTime ExpiresAt { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
