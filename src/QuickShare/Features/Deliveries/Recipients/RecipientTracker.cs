namespace QuickShare.Features.Deliveries.Recipients;

/// <summary>
/// Records what an addressed recipient did with their link (issue #37).
///
/// One place, because three paths reach it — the page, the download and the per-file download —
/// and a rule enforced at each of them separately is the shape of mistake the tier-6 audit spent
/// its time on.
///
/// Everything here is best-effort and never fails a request: a recipient whose row cannot be
/// stamped still gets their file, because the file is the product and the statistic is not.
/// </summary>
public sealed class RecipientTracker(AppDbContext db, ILogger<RecipientTracker> logger)
{
    /// <summary>Stamps the first open. A repeat visit is not a second open.</summary>
    public Task MarkOpenedAsync(Guid deliveryId, string? token, CancellationToken ct) =>
        StampAsync(deliveryId, token, recipient => recipient.OpenedAt ??= DateTime.UtcNow, ct);

    /// <summary>Stamps the first download. Opening is interest; this is delivery.</summary>
    public Task MarkDownloadedAsync(Guid deliveryId, string? token, CancellationToken ct) =>
        StampAsync(deliveryId, token, recipient =>
        {
            // A download implies an open, even when the link was clicked straight through from
            // a mail client that never rendered the page.
            recipient.OpenedAt ??= DateTime.UtcNow;
            recipient.DownloadedAt ??= DateTime.UtcNow;
        }, ct);

    private async Task StampAsync(Guid deliveryId, string? token, Action<DeliveryRecipient> stamp, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        try
        {
            // Scoped by delivery as well as by token: a token from one share must never resolve
            // against another, the same rule the per-file download follows.
            var recipient = await db.DeliveryRecipients
                .FirstOrDefaultAsync(r => r.Token == token && r.DeliveryId == deliveryId, ct);

            if (recipient is null)
                return;

            stamp(recipient);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record recipient activity for delivery {DeliveryId}", deliveryId);
        }
    }
}
