namespace QuickShare.Features.Users.Shared;

/// <summary>
/// Shared user-registration logic used by both the REST endpoint
/// (POST /api/users/register) and the in-process Google OAuth callback.
/// Idempotent: creates the user if missing, otherwise returns the existing one.
/// </summary>
public static class UserRegistrationService
{
    public static async Task<ProfileResponse> RegisterAsync(
        string provider,
        string providerKey,
        string name,
        string email,
        string? avatarUrl,
        AppDbContext db,
        CancellationToken ct,
        AnalyticsService? analytics = null,
        string? referralCode = null)
    {
        var existing = await db.UserLogins
            .Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Provider == provider && l.ProviderKey == providerKey, ct);

        if (existing?.User is not null)
        {
            var u = existing.User;
            if (u.Name != name || u.Email != email)
            {
                u.Name = name;
                u.Email = email;
                await db.SaveChangesAsync(ct);
            }
            return u.ToProfileResponse();
        }

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Email = email,
            AvatarUrl = avatarUrl ?? string.Empty,
            Plan = PlanTierLimits.Free,
            ReferralCode = await ReferralService.GenerateCodeAsync(db, ct)
        };
        db.Users.Add(user);

        db.UserLogins.Add(new UserLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            Provider = provider,
            ProviderKey = providerKey
        });

        var now = DateTime.UtcNow;
        db.UserUsages.Add(new UserUsage
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            PeriodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc),
            TotalBytesUploaded = 0,
            TransferCount = 0
        });

        await db.SaveChangesAsync(ct);

        await ReferralService.ClaimAsync(db, user.Id, referralCode, ct);
        await db.SaveChangesAsync(ct);

        if (analytics is not null)
            await analytics.RecordAsync(AnalyticsEvents.Signup, new AnalyticsEventData(Plan: PlanTierLimits.Free), ct);

        return user.ToProfileResponse();
    }
}
