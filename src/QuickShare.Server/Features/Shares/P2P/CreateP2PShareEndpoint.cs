using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using QuickShare.Server.Data;
using System.Security.Cryptography;

namespace QuickShare.Server.Features.Shares.P2P;

public sealed record CreateP2PShareRequest(string FileName, long FileSize, string MimeType, bool? HasPin);

public sealed record CreateP2PShareResponse(string ShareCode, string ShareLink);

public sealed class CreateP2PShareEndpoint : Endpoint<CreateP2PShareRequest, CreateP2PShareResponse>
{
    public override void Configure()
    {
        Post("/api/shares/p2p");
        Description(b => b.WithTags("P2P"));
        Summary(s =>
        {
            s.Summary = "Create a P2P share link (metadata only)";
            s.Description = "Creates a share link for WebRTC P2P file transfer. No file upload required.";
            s.Responses[201] = "Share created. Returns shareCode and shareLink.";
        });
        AllowAnonymous();
    }

    public override async Task HandleAsync(CreateP2PShareRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
        {
            ThrowError("FileName is required");
            return;
        }

        if (req.FileSize <= 0)
        {
            ThrowError("FileSize must be positive");
            return;
        }

        var shareCode = GenerateShareCode();

        // Ensure uniqueness
        var db = Resolve<AppDbContext>();
        while (await db.Deliveries.AnyAsync(d => d.ShareCode == shareCode, ct))
        {
            shareCode = GenerateShareCode();
        }

        var delivery = new Delivery
        {
            ShareCode = shareCode,
            FileName = req.FileName,
            ContentType = req.MimeType,
            FileSize = req.FileSize,
            TotalBytes = req.FileSize,
            MaxDownloads = 0,
            DownloadCount = 0,
            EgressBytes = 0,
            ExpiresAt = DateTime.UtcNow.AddDays(7)
        };

        db.Deliveries.Add(delivery);
        await db.SaveChangesAsync(ct);

        var baseUrl = Resolve<IConfiguration>()["QuickShare:P2PBaseUrl"]
                       ?? $"{HttpContext.Request.Scheme}://{HttpContext.Request.Host}";
        var shareLink = $"{baseUrl.TrimEnd('/')}/p2p/d/{shareCode}";

        await Send.CreatedAtAsync($"/p2p/d/{shareCode}", new CreateP2PShareResponse(shareCode, shareLink), cancellation: ct);
    }

    private static string GenerateShareCode()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
    }
}
