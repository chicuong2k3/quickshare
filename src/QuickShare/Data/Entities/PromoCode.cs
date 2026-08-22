namespace QuickShare.Data.Entities;

/// <summary>How a promo code reduces a price (issue #98).</summary>
public static class PromoKind
{
    /// <summary>A percentage off, 1–100.</summary>
    public const string Percent = "percent";
    /// <summary>A fixed amount off, in the smallest currency unit.</summary>
    public const string Fixed = "fixed";

    public static readonly string[] All = [Percent, Fixed];
}

/// <summary>
/// A discount code (issue #98).
///
/// Codes are compared case-insensitively and stored upper-cased, because the one thing certain
/// about a code printed on a conference sticker is that somebody will type it in lower case.
/// </summary>
public record PromoCode : BaseAuditEntity
{
    public const int CodeMaxLength = 32;
    public const int DescriptionMaxLength = 120;
    public const int MaxPercent = 100;

    public string Code { get; set; } = string.Empty;
    public string Kind { get; set; } = PromoKind.Percent;

    /// <summary>Percent (1–100) or a fixed amount, depending on <see cref="Kind"/>.</summary>
    public int Value { get; set; }

    public string? Description { get; set; }

    /// <summary>Comma-separated plan names, or null for any paid plan.</summary>
    public string? AppliesToPlans { get; set; }

    /// <summary>Null means unlimited. Counted against <see cref="RedeemedCount"/>.</summary>
    public int? MaxRedemptions { get; set; }
    public int RedeemedCount { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Only for accounts that have never paid. An acquisition code that existing subscribers can
    /// apply is a discount on revenue you already had.
    /// </summary>
    public bool FirstTimeOnly { get; set; }

    public DateTime? RevokedAt { get; set; }

    public bool IsLive(DateTime now) =>
        RevokedAt is null
        && (ExpiresAt is null || ExpiresAt > now)
        && (MaxRedemptions is null || RedeemedCount < MaxRedemptions);

    public bool AppliesTo(string plan) =>
        string.IsNullOrWhiteSpace(AppliesToPlans)
        || AppliesToPlans.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(plan, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The discounted amount. Never below zero, and never above the original — a fixed-amount
    /// code larger than the price must make the purchase free, not negative.
    /// </summary>
    public int ApplyTo(int amount)
    {
        var discount = Kind == PromoKind.Fixed
            ? Value
            : (int)Math.Round(amount * (Value / 100d), MidpointRounding.AwayFromZero);

        return Math.Clamp(amount - discount, 0, amount);
    }

    public static string NormalizeCode(string? raw) =>
        (raw ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>
/// One use of a code by one account (issue #98).
///
/// The unique index on (code, user) is the enforcement, not a report: without it "one per
/// customer" is a check that two concurrent requests both pass.
/// </summary>
public record PromoRedemption
{
    public Guid Id { get; init; }
    public Guid PromoCodeId { get; init; }
    public Guid UserId { get; init; }
    /// <summary>What the discount was worth, for the finance side of the question.</summary>
    public int DiscountAmount { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public PromoCode PromoCode { get; init; } = null!;
}
