using QuickShare.Features.Workspaces.Domains;

namespace QuickShare.Features.Downloads.Shared;

internal static class PreviewAccessGate
{
    public static async Task<string?> CheckAsync(
        Delivery? delivery,
        AppDbContext db,
        DeliveryAccessService access,
        WorkspaceDomainReader domains,
        HttpContext? context,
        CancellationToken ct)
    {
        if (delivery is null || delivery.DeletedAt is not null || delivery.Status == DeliveryStatus.PendingApproval)
        {
            return ErrorCodes.NotFound;
        }

        if (await domains.IsForeignHostAsync(context?.Request.Host.Host, delivery.WorkspaceId, ct))
        {
            return ErrorCodes.NotFound;
        }

        if (await access.CheckAsync(delivery, ct) != DeliveryAccessDecision.Allowed)
        {
            return ErrorCodes.DeliveryRestricted;
        }

        var now = DateTime.UtcNow;
        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked or DeliveryStatus.Expired
            || (delivery.ExpiresAt is { } expiry && expiry <= now))
        {
            return ErrorCodes.DeliveryExpired;
        }

        if (delivery.Status != DeliveryStatus.Active
            && !(delivery.Status == DeliveryStatus.Scheduled && delivery.DeliverAt is { } deliverAt && deliverAt <= now))
        {
            return ErrorCodes.DeliveryGated;
        }

        if (delivery.DownloadCount >= delivery.MaxDownloads)
        {
            return ErrorCodes.DownloadLimitReached;
        }

        if (delivery.DailyDownloadLimit is { } dailyLimit)
        {
            var today = now.Date;
            var count = await db.DownloadTrackings.AsNoTracking()
                .CountAsync(t => t.DeliveryId == delivery.Id && t.DownloadedAt >= today, ct);
            if (count >= dailyLimit)
            {
                return ErrorCodes.DownloadLimitReached;
            }
        }

        if (!string.IsNullOrEmpty(delivery.OtpHash)
            || !string.IsNullOrEmpty(delivery.PasswordHash)
            || delivery.EmailOtpEnabled
            || delivery.RequireRecipientEmail
            || string.IsNullOrEmpty(delivery.StoragePath))
        {
            return ErrorCodes.DeliveryGated;
        }

        return null;
    }
}
