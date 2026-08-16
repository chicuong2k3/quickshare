using LightResults;

namespace QuickShare.Features.Deliveries.Clone;

public record CloneDeliveryCommand(Guid DeliveryId);

public record CloneDeliveryResponse(Guid Id, string ShareCode);

/// <summary>
/// Duplicates a live delivery as a fresh link (issue #218): server-side object copy,
/// new share code, download counter reset, same protections (password/OTP) and message.
/// The clone gets the same expiry window the original had at creation, clamped to the
/// sender's current plan maximum.
/// </summary>
public sealed class CloneDeliveryHandler(IStorageProvider storage)
{
    public async Task<Result<CloneDeliveryResponse>> Handle(
        CloneDeliveryCommand cmd,
        AppDbContext db,
        IHttpContextAccessor http,
        CancellationToken ct)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, ct);

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, ct);

        if (delivery is null)
            return Result.Failure<CloneDeliveryResponse>(new LightResults.Error(ErrorCodes.NotFound));

        // Only live links still have their bytes; expired/revoked storage is gone.
        if (delivery.Status != DeliveryStatus.Active || string.IsNullOrEmpty(delivery.StoragePath))
            return Result.Failure<CloneDeliveryResponse>(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        var newId = Guid.CreateVersion7();
        // Same key shape InitR2MultipartUploadAsync uses.
        var newKey = R2ObjectKey.For(Guid.Empty, newId, delivery.FileName);
        await storage.CopyObjectAsync(delivery.StoragePath, newKey, ct);

        var now = DateTime.UtcNow;
        DateTime? expiresAt = null;
        if (delivery.ExpiresAt is { } originalExpiry)
        {
            var window = originalExpiry - delivery.CreatedAt;
            var cap = PlanTierLimits.GetMaxExpiry(user.Plan);
            if (window <= TimeSpan.Zero || window > cap)
                window = cap;
            expiresAt = now + window;
        }

        var clone = delivery with
        {
            Id = newId,
            ShareCode = ShareCodes.Generate(),
            StoragePath = newKey,
            Status = DeliveryStatus.Active,
            DownloadCount = 0,
            FirstDownloadedAt = null,
            DeliverAt = null,
            ExpiresAt = expiresAt,
            CreatedAt = now,
            UpdatedAt = null,
            Sender = null,
        };

        db.Deliveries.Add(clone);
        await db.SaveChangesAsync(ct);

        return Result.Success(new CloneDeliveryResponse(clone.Id, clone.ShareCode));
    }
}
