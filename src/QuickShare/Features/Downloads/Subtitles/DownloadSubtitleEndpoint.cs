using System.Text;
using QuickShare.Features.Downloads.Bundle;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Infrastructure.Storage;

namespace QuickShare.Features.Downloads.Subtitles;

/// <summary>
/// GET /api/downloads/{ShareCode}/subtitles/{FileId} — serves a <c>.vtt</c> or <c>.srt</c>
/// subtitle file as <c>text/vtt; charset=utf-8</c> (converting SubRip timing lines to WebVTT on
/// the fly) so HTML5 <c>&lt;track kind="subtitles"&gt;</c> elements can render captions directly
/// inside the video preview player (issue #268).
///
/// Enforces <see cref="BundleAccess.ResolveAsync"/> (which checks <see cref="DeliveryAccessService"/>,
/// <c>PendingApproval</c>, expiry, and password/OTP gates) and scopes the file lookup by
/// <c>DeliveryId</c>.
/// </summary>
public sealed class DownloadSubtitleEndpoint : EndpointWithoutRequest
{
    public const long MaxSubtitleBytes = 2 * 1024 * 1024;
    private const string VttContentType = "text/vtt; charset=utf-8";

    public override void Configure()
    {
        Get(RoutePaths.DeliverySubtitlesRoute);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
        Summary(s =>
        {
            s.Summary = "Serve a subtitle track (.srt/.vtt) as WebVTT for video previews";
            s.Description = "Issue #268. Converts .srt timing lines to WebVTT automatically.";
        });
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var db = Resolve<AppDbContext>();
        var delivery = await BundleAccess.ResolveAsync(
            db, Resolve<DeliveryAccessService>(), Route<string>("ShareCode"), ct);

        if (delivery is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var fileId = Route<Guid>("FileId");
        var file = await db.DeliveryFiles
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == fileId && f.DeliveryId == delivery.Id, ct);

        if (file is null || !IsSubtitleFileName(file.FileName) || file.FileSize > MaxSubtitleBytes)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var storage = Resolve<IStorageProvider>();
        await using var stream = await storage.OpenReadAsync(file.ObjectKey, ct);
        if (stream is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var raw = await reader.ReadToEndAsync(ct);

        var vtt = file.FileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase)
            ? ConvertSrtToVtt(raw)
            : EnsureWebVttHeader(raw);

        await Send.StringAsync(vtt, StatusCodes.Status200OK, VttContentType, ct);
    }

    public static bool IsSubtitleFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        return fileName.EndsWith(".vtt", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase);
    }

    public static string EnsureWebVttHeader(string raw)
    {
        var trimmed = raw.TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        if (trimmed.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return "WEBVTT\n\n" + trimmed;
    }

    public static string ConvertSrtToVtt(string srt)
    {
        var normalized = srt.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var sb = new StringBuilder("WEBVTT\n\n");

        foreach (var line in lines)
        {
            if (line.Contains("-->", StringComparison.Ordinal))
            {
                // SubRip uses commas for milliseconds (00:00:01,000 --> 00:00:04,000); WebVTT uses dots.
                sb.Append(line.Replace(',', '.')).Append('\n');
            }
            else
            {
                sb.Append(line).Append('\n');
            }
        }

        return sb.ToString();
    }
}
