namespace QuickShare.Features.Payments.Promos;

/// <summary>What a code is worth to this account for this plan, or why it is not.</summary>
public sealed record PromoQuote(bool Valid, string? Error, int DiscountAmount, int FinalAmount, string? Description);

/// <summary>
/// Validates and redeems discount codes (issue #98).
///
/// Validation and redemption are separate calls on purpose. The checkout screen needs to show a
/// price before the customer commits, and quoting must not consume a limited code — otherwise
/// every abandoned checkout burns a redemption and a hundred-use code runs out on twenty sales.
/// </summary>
public sealed class PromoCodeService(AppDbContext db)
{
    /// <summary>Prices a code without consuming it.</summary>
    public async Task<PromoQuote> QuoteAsync(string? rawCode, Guid userId, string plan, int amount, CancellationToken ct)
    {
        var code = PromoCode.NormalizeCode(rawCode);
        if (code.Length == 0)
            return new PromoQuote(false, ErrorCodes.PromoCodeNotFound, 0, amount, null);

        var promo = await db.PromoCodes.AsNoTracking().FirstOrDefaultAsync(p => p.Code == code, ct);

        // One error for "no such code" and "expired code" alike: telling a stranger which of
        // their guesses exists turns the code list into something worth enumerating.
        if (promo is null || !promo.IsLive(DateTime.UtcNow))
            return new PromoQuote(false, ErrorCodes.PromoCodeNotFound, 0, amount, null);

        if (!promo.AppliesTo(plan))
            return new PromoQuote(false, ErrorCodes.PromoCodeNotApplicable, 0, amount, null);

        var alreadyUsed = await db.PromoRedemptions
            .AsNoTracking()
            .AnyAsync(r => r.PromoCodeId == promo.Id && r.UserId == userId, ct);

        if (alreadyUsed)
            return new PromoQuote(false, ErrorCodes.PromoCodeAlreadyUsed, 0, amount, null);

        if (promo.FirstTimeOnly && await HasEverPaidAsync(userId, ct))
            return new PromoQuote(false, ErrorCodes.PromoCodeFirstTimeOnly, 0, amount, null);

        var final = promo.ApplyTo(amount);
        return new PromoQuote(true, null, amount - final, final, promo.Description);
    }

    /// <summary>
    /// Consumes a code, once payment has actually happened.
    ///
    /// Returns false when the code is no longer usable — the quote could have been minutes ago,
    /// and the last redemption of a limited code can be taken in between. Failing here is not an
    /// error worth blocking a completed payment over; the customer simply pays the full price
    /// they were charged, which is what the payment provider already collected.
    /// </summary>
    public async Task<bool> RedeemAsync(string? rawCode, Guid userId, string plan, int amount, CancellationToken ct)
    {
        var quote = await QuoteAsync(rawCode, userId, plan, amount, ct);
        if (!quote.Valid)
            return false;

        var code = PromoCode.NormalizeCode(rawCode);
        var promo = await db.PromoCodes.FirstOrDefaultAsync(p => p.Code == code, ct);
        if (promo is null || !promo.IsLive(DateTime.UtcNow))
            return false;

        promo.RedeemedCount++;

        db.PromoRedemptions.Add(new PromoRedemption
        {
            Id = Guid.CreateVersion7(),
            PromoCodeId = promo.Id,
            UserId = userId,
            DiscountAmount = quote.DiscountAmount,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index on (code, user) rejected it: two requests raced and the other one
            // won. The database is the enforcement; this catch is what makes it not an exception
            // the customer sees.
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>Whether this account has ever completed a payment, for first-time-only codes.</summary>
    private Task<bool> HasEverPaidAsync(Guid userId, CancellationToken ct) =>
        db.Subscriptions.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.TrialEndsAt == null, ct);
}
