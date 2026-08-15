namespace QuickShare.Features.Deliveries.Otp;

/// <summary>
/// Timing-safe OTP lockout gate (issue #148). Counts failed attempts per
/// delivery and escalates lock duration: 5 fails → 15min, then 1h, then 24h.
/// The check itself never reveals whether a delivery exists — the response
/// shape is identical for unknown and locked deliveries.
/// </summary>
public sealed class OtpLockoutService
{
    public async Task<bool> IsLockedAsync(AppDbContext db, Guid deliveryId, CancellationToken ct)
    {
        var lockout = await db.OtpLockouts
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, ct);

        return lockout?.LockedUntil is { } until && until > DateTime.UtcNow;
    }

    /// <summary>Seconds until the current lock expires, or 0 when not locked.</summary>
    public async Task<int> RetryAfterSecondsAsync(AppDbContext db, Guid deliveryId, CancellationToken ct)
    {
        var lockout = await db.OtpLockouts
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, ct);

        if (lockout?.LockedUntil is not { } until)
            return 0;

        var remaining = (int)Math.Ceiling((until - DateTime.UtcNow).TotalSeconds);
        return Math.Max(0, remaining);
    }

    /// <summary>Records a failed attempt; escalates to the next lock stage when the threshold is crossed.</summary>
    public async Task RecordFailureAsync(AppDbContext db, Guid deliveryId, CancellationToken ct)
    {
        var lockout = await db.OtpLockouts
            .FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, ct);

        if (lockout is null)
        {
            lockout = new OtpLockout
            {
                Id = Guid.CreateVersion7(),
                DeliveryId = deliveryId,
                FailedAttempts = 1,
                LockStage = 0,
                LockedUntil = null,
                UpdatedAt = DateTime.UtcNow
            };
            db.OtpLockouts.Add(lockout);
            await db.SaveChangesAsync(ct);
            return;
        }

        lockout.FailedAttempts++;
        lockout.UpdatedAt = DateTime.UtcNow;

        if (lockout.LockedUntil is null && lockout.FailedAttempts >= OtpLockoutPolicy.MaxFailedAttempts)
        {
            lockout.LockStage = Math.Min(lockout.LockStage, OtpLockoutPolicy.LockDurations.Length - 1);
            lockout.LockedUntil = DateTime.UtcNow.Add(OtpLockoutPolicy.LockDurationForStage(lockout.LockStage));
        }
        else if (lockout.LockedUntil is { } until && until <= DateTime.UtcNow)
        {
            // Previous lock expired and the account failed again → escalate.
            lockout.LockStage = Math.Min(lockout.LockStage + 1, OtpLockoutPolicy.LockDurations.Length - 1);
            lockout.FailedAttempts = 1;
            lockout.LockedUntil = DateTime.UtcNow.Add(OtpLockoutPolicy.LockDurationForStage(lockout.LockStage));
        }

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Clears lockout state after a successful verification.</summary>
    public async Task ResetAsync(AppDbContext db, Guid deliveryId, CancellationToken ct)
    {
        var lockout = await db.OtpLockouts
            .FirstOrDefaultAsync(l => l.DeliveryId == deliveryId, ct);

        if (lockout is not null)
        {
            db.OtpLockouts.Remove(lockout);
            await db.SaveChangesAsync(ct);
        }
    }
}
