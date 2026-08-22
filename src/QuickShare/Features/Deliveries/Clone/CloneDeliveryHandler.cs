using LightResults;

namespace QuickShare.Features.Deliveries.Clone;

public record CloneDeliveryCommand(Guid DeliveryId, Guid? FolderId = null) : IRequest<Result<CloneDeliveryResponse>>;

public record CloneDeliveryResponse(Guid Id, string ShareCode);

/// <summary>
/// Duplicates a live delivery as a fresh link (issue #218): server-side object copy,
/// new share code, download counter reset, same protections (password/OTP) and message.
/// The clone gets the same expiry window the original had at creation, clamped to the
/// sender's current plan maximum.
/// </summary>
public sealed class CloneDeliveryHandler(IStorageProvider storage, AppDbContext db, IHttpContextAccessor http)
    : IRequestHandler<CloneDeliveryCommand, Result<CloneDeliveryResponse>>
{
    public async ValueTask<Result<CloneDeliveryResponse>> Handle(
        CloneDeliveryCommand cmd,
        CancellationToken cancellationToken)
    {
        var user = await http.HttpContext!.User.GetUserAsync(db, cancellationToken);

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == cmd.DeliveryId && d.SenderId == user.Id, cancellationToken);

        if (delivery is null)
            return Result.Failure<CloneDeliveryResponse>(new LightResults.Error(ErrorCodes.NotFound));

        // Only live links still have their bytes; expired/revoked storage is gone.
        if (delivery.Status != DeliveryStatus.Active || string.IsNullOrEmpty(delivery.StoragePath))
            return Result.Failure<CloneDeliveryResponse>(new LightResults.Error(ErrorCodes.DeliveryNotActive));

        var newId = Guid.CreateVersion7();
        // Same key shape InitR2MultipartUploadAsync uses — including the region, so a clone
        // stays in the jurisdiction the original was pinned to (issue #119).
        var newKey = R2ObjectKey.For(Guid.Empty, newId, delivery.FileName, delivery.StorageRegion);
        await storage.CopyObjectAsync(delivery.StoragePath, newKey, cancellationToken);

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

        // "Copy into a folder" (issue #244). Only into a folder of the delivery's own
        // workspace: a copy that lands in another team's folder is a share nobody agreed to,
        // and a personal delivery has no workspace to file it in at all.
        var folderId = delivery.FolderId;
        if (cmd.FolderId is { } destination)
        {
            var reachable = delivery.WorkspaceId is { } workspaceId
                && await db.WorkspaceFolders.AsNoTracking()
                    .AnyAsync(f => f.Id == destination && f.WorkspaceId == workspaceId, cancellationToken);

            if (!reachable)
                return Result.Failure<CloneDeliveryResponse>(new LightResults.Error(ErrorCodes.FolderNotFound));

            folderId = destination;
        }

        var clone = delivery with
        {
            Id = newId,
            FolderId = folderId,
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
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success(new CloneDeliveryResponse(clone.Id, clone.ShareCode));
    }
}
