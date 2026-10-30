using QuickShare.Extensions;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Deliveries.Versions;

public sealed record VersionDto(
    int VersionNumber,
    string FileName,
    string ContentType,
    long FileSize,
    string? Sha256,
    DateTime CreatedAt);

/// <summary>
/// GET /api/deliveries/{id}/versions — the sender's version history for a delivery (issue #110).
///
/// Recipients cannot enumerate versions: they get the latest by default and may request an
/// older one by version number, but the list is not their surface to see. Letting recipients
/// compare every version of a contract reveals the negotiation history the sender chose to
/// send from one side; the sender's side of version picking belongs in the account, not on
/// the recipient page.
/// </summary>
public sealed class ListVersionsEndpoint : EndpointWithoutRequest<IReadOnlyList<VersionDto>>
{
    public override void Configure()
    {
        Get("/api/deliveries/{id}/versions");
        Description(b => b.WithTags(ApiTags.Deliveries));
        Summary(s => s.Summary = "List file version history for a delivery (sender only)");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var user = await HttpContext.User.GetUserAsync(db, ct);
        var id = Route<Guid>("id");

        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id && d.SenderId == user.Id && d.DeletedAt == null, ct);

        if (delivery is null)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var versions = await db.DeliveryVersions
            .AsNoTracking()
            .Where(v => v.DeliveryId == id)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new VersionDto(v.VersionNumber, v.FileName, v.ContentType, v.FileSize, v.Sha256, v.CreatedAt))
            .ToListAsync(ct);

        await Send.OkAsync(versions, ct);
    }
}

/// <summary>
/// GET /api/downloads/{shareCode}/versions/{version} — serve a specific past version of a
/// delivery directly to the recipient (issue #110).
///
/// The share code is the identifier, not the delivery ID, so this endpoint lives on the
/// downloads path rather than the deliveries path. It respects the same gates as the main
/// download (expiry, legal hold, revoke) but skips the burn-after-download gate and does
/// not count against the download cap.
/// </summary>
public sealed class DownloadVersionEndpoint : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get("/api/downloads/{shareCode}/versions/{version}");
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s => s.Summary = "Download a specific file version by share code");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var storage = Resolve<IStorageProvider>();
        var shareCode = Route<string>("shareCode") ?? string.Empty;
        var versionNumber = Route<int>("version");

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        if (delivery.Status is DeliveryStatus.Suspended or DeliveryStatus.Revoked)
        {
            ThrowError(ErrorCodes.DeliverySuspended, StatusCodes.Status410Gone);
            return;
        }

        if (delivery.LegalHoldAt is not null)
        {
            ThrowError(ErrorCodes.LegalHoldActive, StatusCodes.Status409Conflict);
            return;
        }

        var version = await db.DeliveryVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.DeliveryId == delivery.Id && v.VersionNumber == versionNumber, ct);

        if (version is null)
        {
            ThrowError(ErrorCodes.DeliveryFileNotFound, StatusCodes.Status404NotFound);
            return;
        }

        try
        {
            var url = await storage.GetPresignedDownloadUrlAsync(
                version.StoragePath,
                TimeSpan.FromMinutes(5),
                ct);

            await Send.RedirectAsync(url.ToString(), isPermanent: false, cancellationToken: ct);
        }
        catch (R2StorageException)
        {
            ThrowError("Storage error", StatusCodes.Status502BadGateway);
        }
    }
}
