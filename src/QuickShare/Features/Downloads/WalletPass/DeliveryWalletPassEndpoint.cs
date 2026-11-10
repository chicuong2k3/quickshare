using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using QuickShare.Features.Downloads.Bundle;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Features.Workspaces.Domains;

namespace QuickShare.Features.Downloads.WalletPass;

public sealed class DeliveryWalletPassRequest
{
    public string ShareCode { get; set; } = string.Empty;
}

/// <summary>
/// GET /api/downloads/{ShareCode}/wallet-pass — generates an Apple/Google Wallet pass bundle
/// (<c>application/vnd.apple.pkpass</c> ZIP containing <c>pass.json</c> and <c>manifest.json</c>)
/// for a delivery (issue #231).
///
/// Enforces <see cref="DeliveryAccessService"/> (IP allowlist #142, country restriction #166) → 403,
/// <c>PendingApproval</c> (#162) / expired / revoked / suspended → 404, and masks the filename when
/// <c>PrivacyMode</c> (#187) or a password/OTP gate (#44) is active. Never embeds passwords or OTPs.
/// </summary>
public sealed class DeliveryWalletPassEndpoint(
    AppDbContext db,
    DeliveryAccessService access,
    WorkspaceDomainReader domains) : Endpoint<DeliveryWalletPassRequest>
{
    public const string PkPassContentType = "application/vnd.apple.pkpass";

    public override void Configure()
    {
        Get(RoutePaths.DeliveryWalletPassRoute);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Download an Apple/Google Wallet pass (.pkpass) for a delivery";
            s.Description = "Issue #231. Masks file metadata when privacy mode or a password/OTP gate is active.";
        });
    }

    public override async Task HandleAsync(DeliveryWalletPassRequest req, CancellationToken ct)
    {
        if (await access.CheckByCodeAsync(req.ShareCode, ct) != DeliveryAccessDecision.Allowed)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var delivery = await db.Deliveries
            .AsNoTracking()
            .Include(d => d.Sender)
            .ByCode(req.ShareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null
            || delivery.DeletedAt is not null
            || delivery.Status == DeliveryStatus.PendingApproval
            || delivery.Status == DeliveryStatus.Suspended
            || delivery.Status == DeliveryStatus.Revoked
            || (delivery.ExpiresAt.HasValue && delivery.ExpiresAt <= DateTime.UtcNow))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var masked = delivery.PrivacyMode
            || !string.IsNullOrEmpty(delivery.PasswordHash)
            || !string.IsNullOrEmpty(delivery.OtpHash);

        var displayFileName = masked ? "Protected Delivery" : delivery.FileName;
        var sizeLabel = masked ? "Encrypted" : FormatBytes(delivery.FileSize);
        var senderLabel = delivery.PrivacyMode || delivery.Sender is null
            ? "QuickShareCloud"
            : delivery.Sender.Name;
        var shareUrl = await domains.DeliveryUrlAsync(delivery.WorkspaceId, delivery.ShareCode, ct);

        var passObj = new
        {
            formatVersion = 1,
            passTypeIdentifier = "pass.com.quicksharecloud.delivery",
            serialNumber = delivery.ShareCode,
            teamIdentifier = "QSCVN00001",
            organizationName = "QuickShareCloud",
            description = displayFileName,
            logoText = "QuickShareCloud",
            foregroundColor = "rgb(248, 250, 252)",
            backgroundColor = "rgb(15, 23, 42)",
            labelColor = "rgb(148, 163, 184)",
            expirationDate = delivery.ExpiresAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            voided = delivery.Status != DeliveryStatus.Active,
            barcode = new
            {
                format = "PKBarcodeFormatQR",
                message = shareUrl,
                messageEncoding = "iso-8859-1",
                altText = delivery.ShareCode,
            },
            generic = new
            {
                primaryFields = new[]
                {
                    new { key = "file", label = "FILE", value = displayFileName },
                },
                secondaryFields = new[]
                {
                    new { key = "size", label = "SIZE", value = sizeLabel },
                    new { key = "sender", label = "FROM", value = senderLabel },
                },
                auxiliaryFields = new[]
                {
                    new
                    {
                        key = "expires",
                        label = "EXPIRES",
                        value = delivery.ExpiresAt.HasValue
                            ? delivery.ExpiresAt.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                            : "Never",
                    },
                },
            },
        };

        var passJsonBytes = JsonSerializer.SerializeToUtf8Bytes(passObj);
        var passHash = Convert.ToHexString(SHA256.HashData(passJsonBytes)).ToLowerInvariant();
        var manifestJsonBytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["pass.json"] = passHash,
        });

        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var passEntry = archive.CreateEntry("pass.json", CompressionLevel.Fastest);
            await using (var entryStream = passEntry.Open())
            {
                await entryStream.WriteAsync(passJsonBytes, ct);
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Fastest);
            await using (var entryStream = manifestEntry.Open())
            {
                await entryStream.WriteAsync(manifestJsonBytes, ct);
            }
        }

        ms.Position = 0;
        await Send.BytesAsync(
            ms.ToArray(),
            fileName: $"{delivery.ShareCode}.pkpass",
            contentType: PkPassContentType,
            cancellation: ct);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var mag = (int)Math.Floor(Math.Log(bytes, 1024));
        if (mag >= units.Length)
        {
            mag = units.Length - 1;
        }
        var val = bytes / Math.Pow(1024, mag);
        return mag == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : string.Create(CultureInfo.InvariantCulture, $"{val:F1} {units[mag]}");
    }
}
