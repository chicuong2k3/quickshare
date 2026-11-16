using System.Globalization;
using System.Security;
using System.Text;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using QuickShare.Data;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Caching;
using QuickShare.Infrastructure.Constants;

namespace QuickShare.Features.Downloads.Card;

public sealed class DeliveryCardRequest
{
    public string ShareCode { get; set; } = string.Empty;
}

/// <summary>
/// Auto-generated OpenGraph share card SVG per delivery (issue #201).
/// RespectsPendingApproval (404), scope rules via DeliveryAccessService (403),
/// and gated/private shares (masks filename when PasswordHash/OtpHash/PrivacyMode is active).
/// </summary>
public sealed class DeliveryCardEndpoint(
    AppDbContext db,
    CacheService cache,
    DeliveryAccessService access) : Endpoint<DeliveryCardRequest>
{
    private const string SvgContentType = "image/svg+xml; charset=utf-8";

    public override void Configure()
    {
        Get(RoutePaths.DeliveryCardRoute);
        AllowAnonymous();
    }

    public override async Task HandleAsync(DeliveryCardRequest req, CancellationToken ct)
    {
        if (await access.CheckByCodeAsync(req.ShareCode, ct) != DeliveryAccessDecision.Allowed)
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var svg = await cache.GetOrSetAsync(
            $"download:card:{req.ShareCode}",
            async ct2 =>
            {
                var delivery = await db.Deliveries
                    .AsNoTracking()
                    .Include(d => d.Sender)
                    .ByCode(req.ShareCode)
                    .FirstOrDefaultAsync(ct2);

                if (delivery is null
                    || delivery.DeletedAt is not null
                    || delivery.Status == DeliveryStatus.PendingApproval
                    || delivery.Status == DeliveryStatus.Suspended
                    || delivery.Status == DeliveryStatus.Revoked
                    || (delivery.ExpiresAt.HasValue && delivery.ExpiresAt < DateTime.UtcNow))
                {
                    return string.Empty;
                }

                var gated = !string.IsNullOrEmpty(delivery.PasswordHash) || !string.IsNullOrEmpty(delivery.OtpHash);
                var displayFileName = gated ? "Protected delivery" : Truncate(delivery.FileName, 44);
                var sizeText = gated ? "Encrypted" : FormatBytes(delivery.FileSize);
                var expiryText = delivery.ExpiresAt.HasValue
                    ? $"Expires {delivery.ExpiresAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} UTC"
                    : "No expiry";
                var isVerified = !delivery.PrivacyMode
                    && delivery.Sender is not null
                    && delivery.Sender.Plan is PlanTierLimits.Pro or PlanTierLimits.Studio;
                var senderBadge = isVerified
                    ? $"Verified · {Truncate(delivery.Sender!.Name, 24)}"
                    : "QuickShareCloud · AES-256-GCM";

                var sb = new StringBuilder();
                sb.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1200\" height=\"630\" viewBox=\"0 0 1200 630\" role=\"img\" aria-label=\"QuickShareCloud Delivery Card\">");
                sb.AppendLine("  <rect width=\"1200\" height=\"630\" fill=\"#0f172a\"/>");
                sb.AppendLine("  <rect x=\"48\" y=\"48\" width=\"1104\" height=\"534\" rx=\"24\" fill=\"#1e293b\" stroke=\"#334155\" stroke-width=\"2\"/>");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  <text x=\"96\" y=\"140\" fill=\"#38bdf8\" font-family=\"system-ui, sans-serif\" font-size=\"28\" font-weight=\"700\">{EscapeXml(senderBadge)}</text>");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  <text x=\"96\" y=\"280\" fill=\"#f8fafc\" font-family=\"system-ui, sans-serif\" font-size=\"54\" font-weight=\"800\">{EscapeXml(displayFileName)}</text>");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  <text x=\"96\" y=\"360\" fill=\"#94a3b8\" font-family=\"monospace\" font-size=\"32\">{EscapeXml(sizeText)} · {EscapeXml(expiryText)}</text>");
                sb.AppendLine("  <text x=\"96\" y=\"510\" fill=\"#64748b\" font-family=\"system-ui, sans-serif\" font-size=\"24\">QuickShareCloud — Send files, not friction</text>");
                sb.AppendLine("</svg>");
                return sb.ToString();
            },
            duration: TimeSpan.FromSeconds(30),
            token: ct);

        if (string.IsNullOrEmpty(svg))
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.StringAsync(svg, StatusCodes.Status200OK, SvgContentType, ct);
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= max ? value : string.Concat(value.AsSpan(0, max - 1), "…");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var mag = (int)Math.Floor(Math.Log(bytes, 1024));
        if (mag >= units.Length)
            mag = units.Length - 1;
        var val = bytes / Math.Pow(1024, mag);
        return mag == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{bytes} B")
            : string.Create(CultureInfo.InvariantCulture, $"{val:F1} {units[mag]}");
    }

    private static string EscapeXml(string value) => SecurityElement.Escape(value) ?? string.Empty;
}
