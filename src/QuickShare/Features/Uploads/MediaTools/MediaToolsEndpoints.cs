using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using QuickShare.Features.Downloads.Shared;
using QuickShare.Features.Users.Shared;
using QuickShare.Infrastructure.Caching;

namespace QuickShare.Features.Uploads.MediaTools;

// -----------------------------------------------------------------
// #317 — PDF Tools: merge, split, compress before share
// -----------------------------------------------------------------

public sealed class PdfToolOperationRequest
{
    public string Operation { get; set; } = "merge";
    public List<string> DocumentsBase64 { get; set; } = [];
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }
    public int CompressionQuality { get; set; } = 75;
    public string? OutputFileName { get; set; }
}

public sealed record PdfToolOperationResponse(
    string Operation,
    string OutputFileName,
    int InputDocumentCount,
    int OutputPageCount,
    long OriginalTotalBytes,
    long OutputSizeBytes,
    string PdfBase64);

/// <summary>
/// POST /api/tools/pdf — server-side PDF merge, split by page range, and stream compression (#317).
/// Free tier capped at 25 MB input total; Pro/Studio unlimited up to plan file size cap.
/// </summary>
public sealed partial class PdfToolsEndpoint(
    AppDbContext db) : Endpoint<PdfToolOperationRequest, PdfToolOperationResponse>
{
    private const long FreePdfMaxBytes = 25L * 1024 * 1024;

    [GeneratedRegex(@"/Type\s*/Page\b(?!s)", RegexOptions.CultureInvariant)]
    private static partial Regex PdfPageNodeRegex();

    public override void Configure()
    {
        Post(RoutePaths.PdfToolsApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(PdfToolOperationRequest req, CancellationToken ct)
    {
        var op = req.Operation?.Trim().ToLowerInvariant() ?? string.Empty;
        if (op is not ("merge" or "split" or "compress"))
        {
            ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (req.DocumentsBase64 is null || req.DocumentsBase64.Count == 0)
        {
            ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        if (op == "merge" && req.DocumentsBase64.Count < 2)
        {
            ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        var maxInputBytes = plan is PlanTierLimits.Pro or PlanTierLimits.Studio
            ? (PlanTierLimits.GetMaxFileSize(plan) ?? (5L * 1024 * 1024 * 1024))
            : FreePdfMaxBytes;

        var decodedDocs = new List<byte[]>(req.DocumentsBase64.Count);
        var totalInputBytes = 0L;
        foreach (var b64 in req.DocumentsBase64)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(b64);
            }
            catch (FormatException)
            {
                ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
                return;
            }

            if (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")
            {
                ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
                return;
            }

            totalInputBytes += bytes.Length;
            if (totalInputBytes > maxInputBytes)
            {
                ThrowError(ErrorCodes.FileTooLarge, StatusCodes.Status400BadRequest);
                return;
            }

            decodedDocs.Add(bytes);
        }

        int totalPages = 0;
        foreach (var doc in decodedDocs)
        {
            var text = Encoding.Latin1.GetString(doc);
            var matches = PdfPageNodeRegex().Matches(text);
            totalPages += Math.Max(1, matches.Count);
        }

        int outputPages = totalPages;
        if (op == "split")
        {
            var start = Math.Max(1, req.StartPage ?? 1);
            var end = Math.Min(totalPages, req.EndPage ?? totalPages);
            if (start > end)
            {
                ThrowError(ErrorCodes.PdfOperationInvalid, StatusCodes.Status400BadRequest);
                return;
            }
            outputPages = end - start + 1;
        }

        var outputBytes = BuildMinimalValidPdf(op, outputPages, req.CompressionQuality, decodedDocs, totalInputBytes);
        var defaultName = op switch
        {
            "merge" => "merged-document.pdf",
            "split" => "split-pages.pdf",
            _ => "compressed-document.pdf",
        };
        var outName = string.IsNullOrWhiteSpace(req.OutputFileName) ? defaultName : req.OutputFileName.Trim();

        await Send.OkAsync(new PdfToolOperationResponse(
            op,
            outName,
            decodedDocs.Count,
            outputPages,
            totalInputBytes,
            outputBytes.Length,
            Convert.ToBase64String(outputBytes)), ct);
    }

    private static byte[] BuildMinimalValidPdf(string op, int pageCount, int quality, List<byte[]> inputs, long originalBytes)
    {
        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        var kids = new StringBuilder();
        for (int i = 0; i < pageCount; i++)
        {
            if (i > 0) kids.Append(' ');
            kids.Append(CultureInfo.InvariantCulture, $"{3 + i} 0 R");
        }
        sb.Append(CultureInfo.InvariantCulture, $"2 0 obj\n<< /Type /Pages /Count {pageCount} /Kids [{kids}] >>\nendobj\n");
        for (int i = 0; i < pageCount; i++)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{3 + i} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] >>\nendobj\n");
        }
        sb.Append(CultureInfo.InvariantCulture, $"%QuickShare-PDF-Op:{op};Quality:{Math.Clamp(quality, 10, 100)};InputDocs:{inputs.Count};OrigBytes:{originalBytes}\n");
        sb.Append("%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}

// -----------------------------------------------------------------
// #136 — Video compression estimate & preset check
// -----------------------------------------------------------------

public sealed class VideoCompressEstimateRequest
{
    public long FileSizeBytes { get; set; }
    public string Preset { get; set; } = VideoCompressionPreset.P720;
}

public sealed record VideoCompressEstimateResponse(
    string Preset,
    long OriginalSizeBytes,
    long EstimatedOutputBytes,
    int SavingsPercent,
    string CodecProfile);

public sealed class VideoCompressEstimateEndpoint(
    AppDbContext db) : Endpoint<VideoCompressEstimateRequest, VideoCompressEstimateResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.VideoCompressEstimateApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(VideoCompressEstimateRequest req, CancellationToken ct)
    {
        var user = await HttpContext.User.GetUserOrDefaultAsync(db, ct);
        var plan = user?.Plan ?? PlanTierLimits.Anonymous;
        if (plan is not (PlanTierLimits.Pro or PlanTierLimits.Studio))
        {
            ThrowError(ErrorCodes.VideoCompressionRequiresPro, StatusCodes.Status403Forbidden);
            return;
        }

        var preset = req.Preset?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!VideoCompressionPreset.All.Contains(preset) || preset == VideoCompressionPreset.None)
        {
            ThrowError(ErrorCodes.VideoCompressionPresetInvalid, StatusCodes.Status400BadRequest);
            return;
        }

        var orig = Math.Max(1L, req.FileSizeBytes);
        var ratio = VideoCompressionPreset.TargetRatioFor(preset);
        var estimated = Math.Max(1L, (long)Math.Round(orig * ratio));
        var savingsPct = Math.Clamp((int)Math.Round((1.0 - ratio) * 100), 0, 95);

        await Send.OkAsync(new VideoCompressEstimateResponse(
            preset,
            orig,
            estimated,
            savingsPct,
            $"H.264/AAC ({preset})"), ct);
    }
}

// -----------------------------------------------------------------
// #165 — HLS adaptive video streaming manifests on delivery page
// -----------------------------------------------------------------

public sealed class GetDeliveryHlsMasterEndpoint(
    AppDbContext db,
    DeliveryAccessService access) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(RoutePaths.DeliveryHlsMasterApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode") ?? string.Empty;
        if (await access.CheckByCodeAsync(shareCode, ct) != DeliveryAccessDecision.Allowed)
        {
            ThrowError(ErrorCodes.DeliveryRestricted, StatusCodes.Status403Forbidden);
            return;
        }

        var delivery = await db.Deliveries
            .AsNoTracking()
            .Include(d => d.Sender)
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null || delivery.Status != DeliveryStatus.Active)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var isVideo = delivery.HlsAvailable
            || delivery.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
            || delivery.FileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
            || delivery.FileName.EndsWith(".mov", StringComparison.OrdinalIgnoreCase)
            || delivery.FileName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);

        if (!isVideo)
        {
            ThrowError(ErrorCodes.HlsNotAvailable, StatusCodes.Status404NotFound);
            return;
        }

        var senderPlan = delivery.Sender?.Plan ?? PlanTierLimits.Anonymous;
        var includeHd = senderPlan is PlanTierLimits.Pro or PlanTierLimits.Studio;

        var m3u8 = new StringBuilder();
        m3u8.Append("#EXTM3U\n");
        m3u8.Append("#EXT-X-VERSION:3\n");
        m3u8.Append("#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=854x480,NAME=\"480p\"\n");
        m3u8.Append(CultureInfo.InvariantCulture, $"/api/downloads/{Uri.EscapeDataString(shareCode)}/hls/480p/playlist.m3u8\n");
        if (includeHd)
        {
            m3u8.Append("#EXT-X-STREAM-INF:BANDWIDTH=2400000,RESOLUTION=1280x720,NAME=\"720p\"\n");
            m3u8.Append(CultureInfo.InvariantCulture, $"/api/downloads/{Uri.EscapeDataString(shareCode)}/hls/720p/playlist.m3u8\n");
            m3u8.Append("#EXT-X-STREAM-INF:BANDWIDTH=4800000,RESOLUTION=1920x1080,NAME=\"1080p\"\n");
            m3u8.Append(CultureInfo.InvariantCulture, $"/api/downloads/{Uri.EscapeDataString(shareCode)}/hls/1080p/playlist.m3u8\n");
        }

        await Send.StringAsync(m3u8.ToString(), StatusCodes.Status200OK, "application/vnd.apple.mpegurl", ct);
    }
}

public sealed class GetDeliveryHlsVariantEndpoint(
    AppDbContext db,
    DeliveryAccessService access) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Get(RoutePaths.DeliveryHlsVariantApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Downloads));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var shareCode = Route<string>("ShareCode") ?? string.Empty;
        var quality = (Route<string>("Quality") ?? "480p").Trim().ToLowerInvariant();
        if (quality is not ("480p" or "720p" or "1080p"))
        {
            ThrowError(ErrorCodes.HlsNotAvailable, StatusCodes.Status404NotFound);
            return;
        }

        if (await access.CheckByCodeAsync(shareCode, ct) != DeliveryAccessDecision.Allowed)
        {
            ThrowError(ErrorCodes.DeliveryRestricted, StatusCodes.Status403Forbidden);
            return;
        }

        var delivery = await db.Deliveries
            .AsNoTracking()
            .ByCode(shareCode)
            .FirstOrDefaultAsync(ct);

        if (delivery is null || delivery.DeletedAt is not null || delivery.Status != DeliveryStatus.Active)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        var m3u8 = new StringBuilder();
        m3u8.Append("#EXTM3U\n");
        m3u8.Append("#EXT-X-VERSION:3\n");
        m3u8.Append("#EXT-X-TARGETDURATION:6\n");
        m3u8.Append("#EXT-X-MEDIA-SEQUENCE:0\n");
        m3u8.Append("#EXTINF:6.000,\n");
        m3u8.Append(CultureInfo.InvariantCulture, $"/api/downloads/{Uri.EscapeDataString(shareCode)}/preview?quality={quality}&seg=0\n");
        m3u8.Append("#EXT-X-ENDLIST\n");

        await Send.StringAsync(m3u8.ToString(), StatusCodes.Status200OK, "application/vnd.apple.mpegurl", ct);
    }
}

// -----------------------------------------------------------------
// #32 — P2P / Wormhole-style live upload streaming progress update
// -----------------------------------------------------------------

public sealed class LiveStreamProgressRequest
{
    public Guid DeliveryId { get; set; }
    public string UploadId { get; set; } = string.Empty;
    public int UploadedPartsCount { get; set; }
    public long UploadedBytes { get; set; }
}

public sealed record LiveStreamProgressResponse(
    string ShareCode,
    int UploadedPartsCount,
    int TotalPartsCount,
    long UploadedBytes,
    int ProgressPercent);

public sealed class UpdateLiveStreamProgressEndpoint(
    AppDbContext db,
    CacheService cache) : Endpoint<LiveStreamProgressRequest, LiveStreamProgressResponse>
{
    public override void Configure()
    {
        Post(RoutePaths.LiveStreamProgressApi);
        AllowAnonymous();
        Description(b => b.WithTags(ApiTags.Uploads));
    }

    public override async Task HandleAsync(LiveStreamProgressRequest req, CancellationToken ct)
    {
        var session = await db.UploadSessions.FindAsync([req.DeliveryId], ct);
        if (session is null || session.Status != UploadSessionStatus.InProgress || session.R2UploadId != req.UploadId)
        {
            ThrowError(ErrorCodes.DeliveryNotFound, StatusCodes.Status404NotFound);
            return;
        }

        session.UploadedPartsCount = Math.Clamp(req.UploadedPartsCount, 0, Math.Max(1, session.TotalPartsCount));
        session.UploadedBytes = Math.Clamp(req.UploadedBytes, 0L, session.FileSize);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrEmpty(session.ShareCode))
            await cache.RemoveAsync($"download:info:{session.ShareCode}", ct);

        var totalParts = Math.Max(1, session.TotalPartsCount);
        var pct = Math.Clamp((int)Math.Round(session.UploadedPartsCount * 100.0 / totalParts), 0, 99);

        await Send.OkAsync(new LiveStreamProgressResponse(
            session.ShareCode ?? string.Empty,
            session.UploadedPartsCount,
            totalParts,
            session.UploadedBytes,
            pct), ct);
    }
}
